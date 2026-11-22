using System.Collections.ObjectModel;
using System.Diagnostics;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Parsing;
using S7.NetCore.Tools.Validation;

namespace S7.NetCore.Tools.Services;

public sealed class PlcSessionManager : IAsyncDisposable
{
    private readonly OperationLogStore _logStore;
    private readonly ErrorPresentationMapper _errorMapper;
    private readonly DialogService _dialogService;
    private readonly ToolLogSink _sink;
    private readonly SynchronizationContext? _uiContext;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private S7Client? _client;
    private CancellationTokenSource? _connectCancellation;
    private int _state = (int)S7SessionState.Disconnected;
    private int _negotiatedPduSize;
    private int _busy;
    private int _writeUnlocked;
    private int _disposed;
    private int _reconnectAttempt;
    private int _reconnectMaxAttempts;
    private TimeSpan _reconnectDelay;

    public PlcSessionManager(
        OperationLogStore logStore,
        ErrorPresentationMapper errorMapper,
        DialogService dialogService,
        ToolLogSink sink,
        SynchronizationContext? uiContext,
        TimeProvider? timeProvider = null)
    {
        _logStore = logStore;
        _errorMapper = errorMapper;
        _dialogService = dialogService;
        _sink = sink;
        _uiContext = uiContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler? UiStateChanged;

    public S7SessionState State => (S7SessionState)Volatile.Read(ref _state);

    public int NegotiatedPduSize => Volatile.Read(ref _negotiatedPduSize);

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public bool IsWriteUnlocked => Volatile.Read(ref _writeUnlocked) != 0;

    public string BusyText { get; private set; } = string.Empty;

    public int ReconnectAttempt => Volatile.Read(ref _reconnectAttempt);

    public int ReconnectMaxAttempts => Volatile.Read(ref _reconnectMaxAttempts);

    public TimeSpan ReconnectDelay => _reconnectDelay;

    public string ReconnectProgressText
    {
        get
        {
            if (State != S7SessionState.Reconnecting || ReconnectAttempt <= 0)
            {
                return string.Empty;
            }

            return ReconnectMaxAttempts <= 0
                ? $"第 {ReconnectAttempt} 次（不限次数），下次退避 {_reconnectDelay.TotalSeconds:0.#} 秒"
                : $"第 {ReconnectAttempt}/{ReconnectMaxAttempts} 次，下次退避 {_reconnectDelay.TotalSeconds:0.#} 秒";
        }
    }

    public ObservableCollection<SessionTimelineEntry> Timeline { get; } = new();

    public async Task<OperationResult<int>> ConnectAsync(
        ConnectionSettingsModel settings,
        CancellationToken cancellationToken = default)
    {
        if (State is not (S7SessionState.Disconnected or S7SessionState.Faulted))
        {
            return OperationResult<int>.Failed(StateError(S7OperationStage.Connect, "会话已连接或正在连接，请先断开当前会话。"));
        }

        IReadOnlyList<FieldValidationError> validationErrors = ConnectionSettingsValidator.Validate(settings);
        if (validationErrors.Count > 0)
        {
            return OperationResult<int>.Failed(_errorMapper.MapValidation(validationErrors[0]));
        }

        SetBusy(true, "连接中");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _connectCancellation = linked;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        S7Client? client = null;

        try
        {
            S7ConnectionOptions options = ConnectionSettingsValidator.ToOptions(settings);
            client = new S7Client(options, _sink);
            client.SessionStateChanged += OnSessionStateChanged;
            client.ReconnectEvent += OnReconnectEvent;
            lock (_sync)
            {
                _client = client;
            }

            await client.ConnectAsync(linked.Token).ConfigureAwait(false);
            Volatile.Write(ref _negotiatedPduSize, client.NegotiatedPduSize);
            SetState(S7SessionState.Connected);
            AddLog(
                OperationCategory.Connect,
                S7OperationStage.S7Setup,
                true,
                stopwatch.Elapsed,
                $"已连接 {options.IpAddress}:{options.Port}，PDU {client.NegotiatedPduSize}。");
            return OperationResult<int>.Success(client.NegotiatedPduSize);
        }
        catch (S7ClientException exception)
        {
            ErrorPresentation error = _errorMapper.Map(exception, stopwatch.Elapsed);
            if (client is not null)
            {
                client.SessionStateChanged -= OnSessionStateChanged;
                client.ReconnectEvent -= OnReconnectEvent;
                await client.DisposeAsync().ConfigureAwait(false);
                lock (_sync)
                {
                    _client = null;
                }
            }

            SetState(S7SessionState.Disconnected);
            AddLog(OperationCategory.Connect, exception.Stage, false, stopwatch.Elapsed, "连接失败。", error);
            return OperationResult<int>.Failed(error);
        }
        catch (Exception exception)
        {
            ErrorPresentation error = _errorMapper.MapUnknown(exception);
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                lock (_sync)
                {
                    _client = null;
                }
            }

            SetState(S7SessionState.Disconnected);
            AddLog(OperationCategory.Connect, S7OperationStage.Connect, false, stopwatch.Elapsed, "连接失败。", error);
            return OperationResult<int>.Failed(error);
        }
        finally
        {
            SetBusy(false);
            lock (_sync)
            {
                _connectCancellation = null;
            }

            linked.Dispose();
        }
    }

    public async Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? connectCancellation;
        S7Client? client;
        lock (_sync)
        {
            connectCancellation = _connectCancellation;
            client = _client;
        }

        try
        {
            connectCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        if (client is null)
        {
            SetState(S7SessionState.Disconnected);
            AddLog(OperationCategory.Disconnect, S7OperationStage.Disconnect, true, stopwatch.Elapsed, "当前已处于未连接状态。");
            return OperationResult<bool>.Success(true);
        }

        try
        {
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AddLog(
                OperationCategory.Disconnect,
                S7OperationStage.Disconnect,
                false,
                stopwatch.Elapsed,
                "断开时发生异常，仍将释放客户端。",
                _errorMapper.MapUnknown(exception));
        }

        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        client.SessionStateChanged -= OnSessionStateChanged;
        client.ReconnectEvent -= OnReconnectEvent;
        lock (_sync)
        {
            _client = null;
        }

        Volatile.Write(ref _negotiatedPduSize, 0);
        SetState(S7SessionState.Disconnected);
        AddLog(OperationCategory.Disconnect, S7OperationStage.Disconnect, true, stopwatch.Elapsed, "已断开会话。");
        return OperationResult<bool>.Success(true);
    }

    public void SetWriteUnlocked(bool unlocked)
    {
        Volatile.Write(ref _writeUnlocked, unlocked ? 1 : 0);
        RaiseUiStateChanged();
    }

    public async Task<OperationResult<ByteReadOutcome>> ReadBytesAsync(
        ByteAddressSpec spec,
        CancellationToken cancellationToken = default)
    {
        if (GetStateError(S7OperationStage.Read) is { } stateError)
        {
            return OperationResult<ByteReadOutcome>.Failed(stateError);
        }

        if (spec.Length is < 1 or > 65535)
        {
            return OperationResult<ByteReadOutcome>.Failed(ValidationError(
                "Length",
                S7ErrorCode.InvalidLength,
                "读取长度必须在 1 至 65535 之间。"));
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<ByteReadOutcome>.Failed(BusyError(S7OperationStage.Read));
        }

        SetBusy(true, "读取中");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            S7ReadResult result = await _client!.ReadBytesResultAsync(
                new S7ReadRequest(spec.Area, spec.DbNumber, spec.StartByte, spec.Length),
                cancellationToken).ConfigureAwait(false);

            if (result.Data.Length != spec.Length)
            {
                return OperationResult<ByteReadOutcome>.Failed(new ErrorPresentation(
                    S7ErrorCategory.Protocol,
                    S7ErrorCode.ResponseTooShort,
                    S7OperationStage.Read,
                    null,
                    $"返回长度 {result.Data.Length} 与请求长度 {spec.Length} 不一致。",
                    "查看日志并重试。"));
            }

            HexByteCodec.FormattedBytes formatted = HexByteCodec.Format(result.Data.Span, 16);
            var outcome = new ByteReadOutcome(
                spec.Length,
                result.Data.Length,
                formatted.Hex,
                formatted.Ascii,
                result.RecoveryCount,
                result.Retried);
            AddLog(
                OperationCategory.ByteRead,
                S7OperationStage.Read,
                true,
                stopwatch.Elapsed,
                $"读取 {Texts.Describe(spec.Area)} DB{spec.DbNumber} 起始 {spec.StartByte} 长度 {spec.Length} 成功。",
                recovery: result.RecoveryCount,
                retried: result.Retried);
            return OperationResult<ByteReadOutcome>.Success(outcome);
        }
        catch (S7ClientException exception)
        {
            ErrorPresentation error = _errorMapper.Map(exception, stopwatch.Elapsed);
            AddLog(OperationCategory.ByteRead, exception.Stage, false, stopwatch.Elapsed, "字节读取失败。", error);
            return OperationResult<ByteReadOutcome>.Failed(error);
        }
        catch (OperationCanceledException)
        {
            ErrorPresentation error = CancelledError(S7OperationStage.Read);
            AddLog(OperationCategory.ByteRead, S7OperationStage.Read, false, stopwatch.Elapsed, "字节读取已取消。", error);
            return OperationResult<ByteReadOutcome>.Failed(error);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task<OperationResult<ByteWriteOutcome>> WriteBytesAsync(
        ByteAddressSpec spec,
        ReadOnlyMemory<byte> data,
        bool verifyAfterWrite,
        CancellationToken cancellationToken = default)
    {
        if (!IsWriteUnlocked)
        {
            ErrorPresentation error = WriteProtectionError(S7OperationStage.Write);
            AddLog(
                OperationCategory.ByteWrite,
                S7OperationStage.Write,
                false,
                TimeSpan.Zero,
                "写保护锁定，已拦截写入意图。",
                error,
                kind: LogKind.BlockedIntent,
                level: S7LogLevel.Warning);
            return OperationResult<ByteWriteOutcome>.Failed(error);
        }

        if (spec.Area == S7Area.Input)
        {
            ErrorPresentation error = ValidationError(
                "Area",
                S7ErrorCode.AreaNotSupported,
                "PLC 输入区只读，禁止写入。");
            AddLog(OperationCategory.ByteWrite, S7OperationStage.Validate, false, TimeSpan.Zero, "输入区写入被拒绝。", error);
            return OperationResult<ByteWriteOutcome>.Failed(error);
        }

        if (data.Length is < 1 or > 65535)
        {
            return OperationResult<ByteWriteOutcome>.Failed(ValidationError(
                "Data",
                S7ErrorCode.InvalidLength,
                "写入字节数必须在 1 至 65535 之间。"));
        }

        if (data.Length > 512)
        {
            bool confirmed = await _dialogService.ConfirmAsync(
                "确认写入",
                $"将向 {Texts.Describe(spec.Area)} DB{spec.DbNumber} 起始 {spec.StartByte} 写入 {data.Length} 字节，是否继续？",
                cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                ErrorPresentation error = CancelledError(S7OperationStage.Write);
                return OperationResult<ByteWriteOutcome>.Failed(error);
            }
        }

        if (GetStateError(S7OperationStage.Write) is { } stateError)
        {
            return OperationResult<ByteWriteOutcome>.Failed(stateError);
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<ByteWriteOutcome>.Failed(BusyError(S7OperationStage.Write));
        }

        SetBusy(true, "写入中");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            S7WriteResult result = await _client!.WriteBytesResultAsync(
                new S7WriteRequest(spec.Area, spec.DbNumber, spec.StartByte, data),
                cancellationToken).ConfigureAwait(false);

            bool verifyPerformed = false;
            string? difference = null;
            if (verifyAfterWrite)
            {
                verifyPerformed = true;
                S7ReadResult readBack = await _client.ReadBytesResultAsync(
                    new S7ReadRequest(spec.Area, spec.DbNumber, spec.StartByte, data.Length),
                    cancellationToken).ConfigureAwait(false);
                difference = Compare(data.Span, readBack.Data.Span);
            }

            var outcome = new ByteWriteOutcome(
                data.Length,
                result.BytesWritten,
                result.RecoveryCount,
                result.Retried,
                result.ReplayAttempted,
                result.ReplayedBytes,
                false,
                null,
                null,
                verifyPerformed,
                difference);
            AddLog(
                OperationCategory.ByteWrite,
                S7OperationStage.Write,
                true,
                stopwatch.Elapsed,
                $"写入 {data.Length} 字节成功"
                + (difference is null ? string.Empty : $"，回读差异：{difference}"),
                recovery: result.RecoveryCount,
                retried: result.Retried,
                replay: result.ReplayAttempted);
            return OperationResult<ByteWriteOutcome>.Success(outcome);
        }
        catch (S7ClientException exception)
        {
            ErrorPresentation error = _errorMapper.Map(exception, stopwatch.Elapsed);
            if (exception.BytesWritten > 0)
            {
                error = error with { Message = error.Message + $" 已确认写入 {exception.BytesWritten} 字节。" };
            }

            AddLog(OperationCategory.ByteWrite, exception.Stage, false, stopwatch.Elapsed, "字节写入失败。", error);
            return OperationResult<ByteWriteOutcome>.Failed(error);
        }
        catch (OperationCanceledException)
        {
            ErrorPresentation error = CancelledError(S7OperationStage.Write);
            AddLog(OperationCategory.ByteWrite, S7OperationStage.Write, false, stopwatch.Elapsed, "字节写入已取消。", error);
            return OperationResult<ByteWriteOutcome>.Failed(error);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task<OperationResult<TypedReadOutcome>> ReadValueAsync(
        TypedAddressSpec spec,
        CancellationToken cancellationToken = default)
    {
        TypedValidationResult validation = TypedInputValidator.Validate(spec);
        if (!validation.IsValid)
        {
            return OperationResult<TypedReadOutcome>.Failed(_errorMapper.MapValidation(validation.Error!));
        }

        if (GetStateError(S7OperationStage.Read) is { } stateError)
        {
            return OperationResult<TypedReadOutcome>.Failed(stateError);
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<TypedReadOutcome>.Failed(BusyError(S7OperationStage.Read));
        }

        SetBusy(true, "读取中");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            S7TypedReadResult result = await _client!.ReadValueAsync(
                new S7TypedReadRequest(spec.Expression, spec.DataType, spec.Capacity),
                cancellationToken).ConfigureAwait(false);

            string valueText = FormatValue(spec.DataType, result.Value);
            string? stringValue = spec.DataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString
                ? result.Value.AsString()
                : null;

            var outcome = new TypedReadOutcome(
                valueText,
                result.BytesOccupied,
                result.Address.Expression ?? spec.Expression,
                result.Address.BitNumber,
                stringValue?.Length,
                spec.Capacity > 0 ? spec.Capacity : null,
                result.RecoveryCount,
                result.Retried);
            AddLog(
                OperationCategory.TypedRead,
                S7OperationStage.Read,
                true,
                stopwatch.Elapsed,
                $"类型化读取 {spec.Expression}（{Texts.Describe(spec.DataType)}）成功。",
                recovery: result.RecoveryCount,
                retried: result.Retried);
            return OperationResult<TypedReadOutcome>.Success(outcome);
        }
        catch (S7ClientException exception)
        {
            ErrorPresentation error = _errorMapper.Map(exception, stopwatch.Elapsed);
            AddLog(OperationCategory.TypedRead, exception.Stage, false, stopwatch.Elapsed, "类型化读取失败。", error);
            return OperationResult<TypedReadOutcome>.Failed(error);
        }
        catch (OperationCanceledException)
        {
            ErrorPresentation error = CancelledError(S7OperationStage.Read);
            AddLog(OperationCategory.TypedRead, S7OperationStage.Read, false, stopwatch.Elapsed, "类型化读取已取消。", error);
            return OperationResult<TypedReadOutcome>.Failed(error);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task<OperationResult<TypedWriteOutcome>> WriteValueAsync(
        TypedAddressSpec spec,
        S7Value value,
        CancellationToken cancellationToken = default)
    {
        if (!IsWriteUnlocked)
        {
            ErrorPresentation error = WriteProtectionError(S7OperationStage.Write);
            AddLog(
                OperationCategory.TypedWrite,
                S7OperationStage.Write,
                false,
                TimeSpan.Zero,
                "写保护锁定，已拦截类型化写入意图。",
                error,
                kind: LogKind.BlockedIntent,
                level: S7LogLevel.Warning);
            return OperationResult<TypedWriteOutcome>.Failed(error);
        }

        TypedValidationResult validation = TypedInputValidator.Validate(spec);
        if (!validation.IsValid)
        {
            return OperationResult<TypedWriteOutcome>.Failed(_errorMapper.MapValidation(validation.Error!));
        }

        if (validation.OccupiedBytes > 512)
        {
            bool confirmed = await _dialogService.ConfirmAsync(
                "确认写入",
                $"将向 {spec.Expression} 写入 {validation.OccupiedBytes} 字节，是否继续？",
                cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                return OperationResult<TypedWriteOutcome>.Failed(CancelledError(S7OperationStage.Write));
            }
        }

        if (GetStateError(S7OperationStage.Write) is { } stateError)
        {
            return OperationResult<TypedWriteOutcome>.Failed(stateError);
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<TypedWriteOutcome>.Failed(BusyError(S7OperationStage.Write));
        }

        SetBusy(true, "写入中");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            S7WriteResult result = await _client!.WriteValueResultAsync(
                new S7TypedWriteRequest(spec.Expression, spec.DataType, value, spec.Capacity),
                cancellationToken).ConfigureAwait(false);

            var outcome = new TypedWriteOutcome(
                result.BytesWritten,
                result.RecoveryCount,
                result.Retried,
                result.ReplayAttempted,
                result.ReplayedBytes);
            AddLog(
                OperationCategory.TypedWrite,
                S7OperationStage.Write,
                true,
                stopwatch.Elapsed,
                $"类型化写入 {spec.Expression}（{Texts.Describe(spec.DataType)}）成功。",
                recovery: result.RecoveryCount,
                retried: result.Retried,
                replay: result.ReplayAttempted);
            return OperationResult<TypedWriteOutcome>.Success(outcome);
        }
        catch (S7ClientException exception)
        {
            ErrorPresentation error = _errorMapper.Map(exception, stopwatch.Elapsed);
            if (exception.BytesWritten > 0)
            {
                error = error with { Message = error.Message + $" 已确认写入 {exception.BytesWritten} 字节。" };
            }

            AddLog(OperationCategory.TypedWrite, exception.Stage, false, stopwatch.Elapsed, "类型化写入失败。", error);
            return OperationResult<TypedWriteOutcome>.Failed(error);
        }
        catch (OperationCanceledException)
        {
            ErrorPresentation error = CancelledError(S7OperationStage.Write);
            AddLog(OperationCategory.TypedWrite, S7OperationStage.Write, false, stopwatch.Elapsed, "类型化写入已取消。", error);
            return OperationResult<TypedWriteOutcome>.Failed(error);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await DisconnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        _gate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    private void OnSessionStateChanged(object? sender, S7SessionStateChangedEventArgs e)
    {
        PostUi(() =>
        {
            Volatile.Write(ref _state, (int)e.CurrentState);
            if (e.CurrentState == S7SessionState.Connected)
            {
                Volatile.Write(ref _negotiatedPduSize, _client?.NegotiatedPduSize ?? 0);
            }
            else if (e.CurrentState == S7SessionState.Disconnected)
            {
                Volatile.Write(ref _negotiatedPduSize, 0);
            }

            string reason = e.FirstFaultErrorCode is not null
                ? $"错误码 {(int)e.FirstFaultErrorCode.Value}"
                : e.CurrentState == S7SessionState.Reconnecting
                    ? "连接中断，开始自动重连"
                    : "状态变更";
            Timeline.Insert(0, new SessionTimelineEntry(
                e.Timestamp.ToLocalTime(),
                e.PreviousState,
                e.CurrentState,
                reason));
            RaiseUiStateChanged();
        });
    }

    private void OnReconnectEvent(object? sender, S7ReconnectEventArgs e)
    {
        PostUi(() =>
        {
            Volatile.Write(ref _reconnectAttempt, e.Attempt);
            Volatile.Write(ref _reconnectMaxAttempts, e.MaxAttempts);
            _reconnectDelay = e.Delay;

            if (e.Kind == S7ReconnectEventKind.Reconnected && e.NegotiatedPduSize is int pdu)
            {
                Volatile.Write(ref _negotiatedPduSize, pdu);
            }

            AddLog(
                OperationCategory.Reconnect,
                S7OperationStage.Reconnect,
                e.Kind is S7ReconnectEventKind.AttemptStarted or S7ReconnectEventKind.Reconnected,
                e.Elapsed,
                $"自动重连：{Texts.Describe(e.Kind)}，第 {e.Attempt} 次"
                + (e.MaxAttempts == 0 ? "（不限次数）。" : $"/{e.MaxAttempts} 次。"),
                e.ErrorCode is null ? null : new ErrorPresentation(
                    e.ErrorCategory ?? S7ErrorCategory.Connection,
                    e.ErrorCode,
                    S7OperationStage.Reconnect,
                    null,
                    Texts.Describe(e.Kind),
                    "等待自动重连或手动断开。"),
                level: e.Kind is S7ReconnectEventKind.AttemptFailed or S7ReconnectEventKind.Exhausted
                    ? S7LogLevel.Warning
                    : S7LogLevel.Information);
            RaiseUiStateChanged();
        });
    }

    private void PostUi(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ =>
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                action();
            }
        }, null);
    }

    private void SetState(S7SessionState state)
    {
        Volatile.Write(ref _state, (int)state);
        if (state == S7SessionState.Disconnected)
        {
            Volatile.Write(ref _negotiatedPduSize, 0);
        }

        RaiseUiStateChanged();
    }

    private void SetBusy(bool value, string text = "")
    {
        Volatile.Write(ref _busy, value ? 1 : 0);
        BusyText = text;
        RaiseUiStateChanged();
    }

    private void RaiseUiStateChanged()
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            UiStateChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _uiContext.Post(_ => UiStateChanged?.Invoke(this, EventArgs.Empty), null);
        }
    }

    private ErrorPresentation? GetStateError(S7OperationStage stage)
    {
        S7SessionState state = State;
        if (state == S7SessionState.Connected)
        {
            return null;
        }

        return state == S7SessionState.Reconnecting
            ? new ErrorPresentation(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionReconnecting,
                stage,
                null,
                "正在自动重连，暂不可读写（错误码 204）。",
                "等待重连完成，或断开后手动重新连接。")
            : new ErrorPresentation(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionNotEstablished,
                stage,
                null,
                "会话未建立，请先连接 PLC。",
                "请检查连接参数并连接。");
    }

    private static ErrorPresentation StateError(S7OperationStage stage, string message)
    {
        return new ErrorPresentation(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.SessionAlreadyConnected,
            stage,
            null,
            message,
            "请先断开当前会话。");
    }

    private static ErrorPresentation BusyError(S7OperationStage stage)
    {
        return new ErrorPresentation(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.SessionFaulted,
            stage,
            null,
            "另一操作正在进行，请稍候。",
            "等待当前操作完成后重试。");
    }

    private static ErrorPresentation WriteProtectionError(S7OperationStage stage)
    {
        return new ErrorPresentation(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.SessionFaulted,
            stage,
            null,
            "写保护已锁定，禁止写入。",
            "先在主窗口解锁写保护。");
    }

    private static ErrorPresentation CancelledError(S7OperationStage stage)
    {
        return new ErrorPresentation(
            S7ErrorCategory.Cancelled,
            S7ErrorCode.OperationCancelled,
            stage,
            null,
            "操作已取消。",
            "如需继续，请重新提交操作。");
    }

    private static ErrorPresentation ValidationError(string field, S7ErrorCode code, string message)
    {
        return new ErrorPresentation(
            S7ErrorCategory.InvalidArgument,
            code,
            S7OperationStage.Validate,
            null,
            message,
            $"请修正字段 {field} 后重试。");
    }

    private void AddLog(
        OperationCategory category,
        S7OperationStage stage,
        bool succeeded,
        TimeSpan elapsed,
        string summary,
        ErrorPresentation? error = null,
        int? recovery = null,
        bool retried = false,
        bool replay = false,
        LogKind kind = LogKind.Operation,
        S7LogLevel? level = null)
    {
        var entry = new OperationLogEntry(
            _timeProvider.GetLocalNow(),
            kind,
            level ?? (succeeded ? S7LogLevel.Information : S7LogLevel.Warning),
            category,
            stage,
            succeeded,
            elapsed,
            error?.Category,
            error?.Code,
            error?.PlcReturnCode,
            recovery,
            retried,
            replay,
            summary);
        _logStore.Append(entry);
    }

    private static string FormatValue(S7DataType dataType, S7Value value)
    {
        return dataType switch
        {
            S7DataType.Bool => value.AsBoolean() ? "true (1)" : "false (0)",
            S7DataType.Byte => value.AsByte().ToString(),
            S7DataType.Word => value.AsUInt16().ToString(),
            S7DataType.Int => value.AsInt16().ToString(),
            S7DataType.DWord => value.AsUInt32().ToString(),
            S7DataType.DInt => value.AsInt32().ToString(),
            S7DataType.Real => value.AsSingle().ToString("G9"),
            S7DataType.LReal => value.AsDouble().ToString("G17"),
            S7DataType.String or S7DataType.S7String or S7DataType.S7WString => value.AsString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string? Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int length = Math.Min(expected.Length, actual.Length);
        int differences = 0;
        int firstIndex = -1;
        byte firstExpected = 0;
        byte firstActual = 0;

        for (int i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
            {
                if (differences == 0)
                {
                    firstIndex = i;
                    firstExpected = expected[i];
                    firstActual = actual[i];
                }

                differences++;
            }
        }

        if (differences == 0 && expected.Length == actual.Length)
        {
            return null;
        }

        if (firstIndex < 0)
        {
            return $"长度不一致：期望 {expected.Length}，实际 {actual.Length}";
        }

        return $"偏移 {firstIndex}，期望 0x{firstExpected:X2}，实际 0x{firstActual:X2}；差异总数 {differences}";
    }
}
