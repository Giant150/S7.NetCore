using System.Buffers;

namespace S7.NetCore.Internal;

/// <summary>
/// S7 会话：状态机、超时编排、分包读写、结果装配与可选断线自动重连。
/// </summary>
internal sealed partial class S7Session : IAsyncDisposable
{
    private readonly S7ConnectionOptions _options;
    private readonly S7ReconnectPolicy _reconnect;
    private readonly IS7Logger _logger;
    private readonly ITcpTransport? _injectedTransport;
    private readonly Func<ITcpTransport>? _transportFactory;
    private readonly RequestGate _gate;
    private readonly PduReferenceGenerator _references = new();
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();

    private int _state = (int)S7SessionState.Disconnected;
    private int _disposed;
    private int _negotiatedPdu;
    private Guid _sessionId = Guid.Empty;
    private volatile ITcpTransport? _transport;
    private int _reconnectAttempt;
    private DateTimeOffset? _lastReconnectAt;
    private TimeSpan? _nextReconnectDelay;
    private S7ErrorCode? _firstFaultErrorCode;
    private CancellationTokenSource? _reconnectCancellation;
    private Task? _reconnectTask;
    private TaskCompletionSource<ReconnectOutcome>? _reconnectSignal;
    private S7ReconnectAbortReason _reconnectAbortReason = S7ReconnectAbortReason.DisconnectedByCaller;

    private enum ReconnectOutcome
    {
        Reconnected,
        Exhausted,
        Aborted
    }

    internal S7Session(
        S7ConnectionOptions options,
        IS7Logger? logger,
        ITcpTransport? transport,
        TimeProvider? timeProvider,
        Func<ITcpTransport>? transportFactory = null)
    {
        _options = options;
        _reconnect = options.Reconnect;
        _logger = logger is null or NullS7Logger ? NullS7Logger.Instance : new SafeS7Logger(logger);
        _injectedTransport = transport;
        _transportFactory = transportFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _gate = new RequestGate(options.MaxQueuedRequests);
    }

    internal event EventHandler<S7SessionStateChangedEventArgs>? StateChanged;

    internal event EventHandler<S7ReconnectEventArgs>? ReconnectEventRaised;

    internal S7SessionState State => (S7SessionState)Volatile.Read(ref _state);

    internal int NegotiatedPduSize => Volatile.Read(ref _negotiatedPdu);

    internal SessionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return CreateSnapshotLocked();
            }
        }
    }

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (State == S7SessionState.Reconnecting)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionAlreadyConnected,
                S7OperationStage.Connect,
                "会话正在自动重连中，禁止建立第二条并行会话。");
        }

        if (!TryTransition(
                S7SessionState.Disconnected,
                S7SessionState.Connecting,
                0,
                null,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _))
        {
            bool faulted = State == S7SessionState.Faulted;
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                faulted ? S7ErrorCode.SessionFaulted : S7ErrorCode.SessionAlreadyConnected,
                S7OperationStage.Connect,
                faulted
                    ? "会话处于异常状态，必须显式断开后重连。"
                    : "会话已存在或正在连接中，禁止建立第二条并行会话。");
        }

        var scope = new OperationScope();
        ITcpTransport transport = CreateTransport();
        _transport = transport;

        try
        {
            PduNegotiation negotiation = await HandshakeAsync(transport, cancellationToken).ConfigureAwait(false);
            if (!TryTransition(
                    S7SessionState.Connecting,
                    S7SessionState.Connected,
                    0,
                    null,
                    Guid.NewGuid(),
                    negotiation.Effective,
                    null,
                    null,
                    out _))
            {
                _transport = null;
                await transport.DisposeAsync().ConfigureAwait(false);
                return;
            }

            Log(scope, "Connect", S7OperationStage.S7Setup, succeeded: true, negotiatedPduSize: negotiation.Effective);
        }
        catch (S7ClientException exception)
        {
            await FailConnectAsync(transport).ConfigureAwait(false);
            LogFailure(scope, "Connect", exception.Stage, exception);
            throw;
        }
    }

    internal async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (State == S7SessionState.Disconnected)
        {
            CleanupReconnectArtifacts();
            return;
        }

        var scope = new OperationScope();

        if (State == S7SessionState.Reconnecting)
        {
            await AbortReconnectAsync(S7ReconnectAbortReason.DisconnectedByCaller).ConfigureAwait(false);

            ITcpTransport? reconnectingTransport = _transport;
            _transport = null;
            if (reconnectingTransport is not null)
            {
                await reconnectingTransport.DisposeAsync().ConfigureAwait(false);
            }

            Volatile.Write(ref _negotiatedPdu, 0);
            _sessionId = Guid.Empty;
            Log(scope, "Disconnect", S7OperationStage.Disconnect, succeeded: true);
            return;
        }

        RequestGate.Lease? lease = null;
        try
        {
            lease = await _gate.AcquireAsync(S7OperationStage.Disconnect, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 调用方取消等待在途请求时仍然强制关闭，避免遗留半开连接。
        }

        try
        {
            S7SessionState previous = State;
            ITcpTransport? transport = _transport;
            _transport = null;
            Volatile.Write(ref _negotiatedPdu, 0);
            _sessionId = Guid.Empty;

            TryTransition(
                previous,
                S7SessionState.Disconnected,
                0,
                null,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _);

            if (transport is not null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            lease?.Dispose();
            Log(scope, "Disconnect", S7OperationStage.Disconnect, succeeded: true);
        }
    }

    internal async Task<ReadOnlyMemory<byte>> ReadAsync(S7ReadRequest request, CancellationToken cancellationToken)
    {
        S7ReadResult result = await ReadResultAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Data;
    }

    internal async Task<S7ReadResult> ReadResultAsync(S7ReadRequest request, CancellationToken cancellationToken)
    {
        int recovery = 0;
        while (true)
        {
            try
            {
                ReadOnlyMemory<byte> data = await ReadOnceAsync(request, recovery == 0, cancellationToken)
                    .ConfigureAwait(false);
                return new S7ReadResult(data, recovery, recovery > 0);
            }
            catch (S7ClientException exception) when (recovery == 0 && ShouldRecover(exception))
            {
                await WaitForRecoveryAsync(S7OperationStage.Read, cancellationToken).ConfigureAwait(false);
                recovery = 1;
            }
        }
    }

    private async Task<ReadOnlyMemory<byte>> ReadOnceAsync(
        S7ReadRequest request,
        bool allowWait,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Read, allowWait, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Read,
                DescribeReadChunk(request, request.StartByte, request.Length),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Read, allowWait: false, cancellationToken).ConfigureAwait(false);

        int payloadLimit = NegotiatedPduSize - ProtocolConstants.ReadResponseOverhead;
        int totalLength = request.Length;
        int chunkCount = (totalLength + payloadLimit - 1) / payloadLimit;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        var scope = new OperationScope();
        ITcpTransport transport = _transport!;
        int offset = 0;
        int chunkIndex = 0;

        try
        {
            while (offset < totalLength)
            {
                chunkIndex++;
                int chunkLength = Math.Min(payloadLimit, totalLength - offset);
                int chunkStart = request.StartByte + offset;

                using var timeout = new OperationTimeout(_options.ReadTimeout, cancellationToken, _timeProvider);
                ushort pduReference = _references.Next();
                byte[] frame = S7RequestBuilder.BuildReadRequest(
                    request.Area,
                    request.DbNumber,
                    chunkStart,
                    chunkLength,
                    pduReference);

                try
                {
                    TraceFrame("Read", S7OperationStage.Read, frame);
                    await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
                    byte[] payload = await S7FrameReader
                        .ReadS7PayloadAsync(transport, S7OperationStage.Read, timeout.Token)
                        .ConfigureAwait(false);

                    ReadResponseInfo info = S7ResponseParser.ParseReadResponse(payload, pduReference, chunkLength);
                    if (info.ReturnCode != ProtocolConstants.SuccessReturnCode)
                    {
                        throw CreatePlcException(
                            info.ReturnCode,
                            S7OperationStage.Read,
                            DescribeReadChunk(request, chunkStart, chunkLength));
                    }

                    payload.AsSpan(info.DataOffset, chunkLength).CopyTo(buffer.AsSpan(offset, chunkLength));
                    offset += chunkLength;
                    LogChunk(
                        scope,
                        "Read",
                        S7OperationStage.Read,
                        request.Area,
                        request.DbNumber,
                        chunkStart,
                        chunkLength,
                        chunkIndex,
                        chunkCount);
                }
                catch (OperationCanceledException ex)
                {
                    bool drained = await DrainAsync(transport, pduReference, S7OperationStage.Read)
                        .ConfigureAwait(false);

                    if (timeout.CallerTriggered)
                    {
                        if (!drained)
                        {
                            await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                        }
                    }
                    else if (_reconnect.ReconnectOnTimeout)
                    {
                        await HandleFaultAsync(
                                transport,
                                S7OperationStage.Read,
                                S7ErrorCode.ReadTimeout,
                                S7ErrorCategory.Timeout,
                                callerCancelled: false)
                            .ConfigureAwait(false);
                    }
                    else if (!drained)
                    {
                        await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                    }

                    S7ClientException mapped = MapCanceled(
                        timeout,
                        S7OperationStage.Read,
                        S7ErrorCode.ReadTimeout,
                        DescribeReadChunk(request, chunkStart, chunkLength),
                        ex);
                    LogFailure(
                        scope,
                        "Read",
                        S7OperationStage.Read,
                        mapped,
                        request.Area,
                        request.DbNumber,
                        chunkStart,
                        chunkLength);
                    throw mapped;
                }
                catch (TransportException ex)
                {
                    await HandleFaultAsync(
                            transport,
                            S7OperationStage.Read,
                            S7ErrorCode.ConnectionReset,
                            S7ErrorCategory.Connection,
                            callerCancelled: false)
                        .ConfigureAwait(false);

                    var mapped = new S7ClientException(
                        S7ErrorCategory.Connection,
                        S7ErrorCode.ConnectionReset,
                        S7OperationStage.Read,
                        $"读取过程中连接中断：{DescribeReadChunk(request, chunkStart, chunkLength)}。",
                        innerException: ex);
                    LogFailure(
                        scope,
                        "Read",
                        S7OperationStage.Read,
                        mapped,
                        request.Area,
                        request.DbNumber,
                        chunkStart,
                        chunkLength);
                    throw mapped;
                }
                catch (S7ClientException ex)
                {
                    LogFailure(
                        scope,
                        "Read",
                        S7OperationStage.Read,
                        ex,
                        request.Area,
                        request.DbNumber,
                        chunkStart,
                        chunkLength);
                    throw;
                }
            }

            var result = new byte[totalLength];
            buffer.AsSpan(0, totalLength).CopyTo(result);
            Log(
                scope,
                "Read",
                S7OperationStage.Read,
                succeeded: true,
                request.Area,
                request.DbNumber,
                request.StartByte,
                totalLength,
                chunkCount: chunkCount);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal async Task<int> WriteBitAsync(
        S7WriteRequest request,
        int bitNumber,
        bool value,
        CancellationToken cancellationToken)
    {
        S7WriteResult result = await WriteBitResultAsync(request, bitNumber, value, cancellationToken)
            .ConfigureAwait(false);
        return result.BytesWritten;
    }

    internal async Task<S7WriteResult> WriteBitResultAsync(
        S7WriteRequest request,
        int bitNumber,
        bool value,
        CancellationToken cancellationToken)
    {
        int recovery = 0;
        bool replayAttempted = false;

        while (true)
        {
            try
            {
                int written = await WriteBitOnceAsync(request, bitNumber, value, recovery == 0, cancellationToken)
                    .ConfigureAwait(false);
                return new S7WriteResult(written, recovery, recovery > 0, replayAttempted, replayAttempted ? 1 : 0);
            }
            catch (S7ClientException exception) when (recovery == 0 && ShouldRecover(exception))
            {
                if (exception.WriteOutcomeUncertain && !_reconnect.ReplayUncertainWrites)
                {
                    throw;
                }

                replayAttempted = exception.WriteOutcomeUncertain;
                await WaitForRecoveryAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
                recovery = 1;
            }
        }
    }

    private async Task<int> WriteBitOnceAsync(
        S7WriteRequest request,
        int bitNumber,
        bool value,
        bool allowWait,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Write, allowWait, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, 1),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Write, allowWait: false, cancellationToken).ConfigureAwait(false);

        var scope = new OperationScope();
        ITcpTransport transport = _transport!;
        using var timeout = new OperationTimeout(_options.WriteTimeout, cancellationToken, _timeProvider);
        ushort pduReference = _references.Next();
        byte[] frame = S7RequestBuilder.BuildWriteBitRequest(
            request.Area,
            request.DbNumber,
            request.StartByte,
            bitNumber,
            value,
            pduReference);
        bool writeIssued = false;

        try
        {
            TraceFrame("Write", S7OperationStage.Write, frame);
            await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            writeIssued = true;
            byte[] payload = await S7FrameReader
                .ReadS7PayloadAsync(transport, S7OperationStage.Write, timeout.Token)
                .ConfigureAwait(false);

            WriteResponseInfo info = S7ResponseParser.ParseWriteResponse(payload, pduReference);
            if (info.ReturnCode != ProtocolConstants.SuccessReturnCode)
            {
                (S7ErrorCode code, string message) = PlcReturnCodeMapper.Map(info.ReturnCode);
                throw new S7ClientException(
                    S7ErrorCategory.PlcReturn,
                    code,
                    S7OperationStage.Write,
                    $"位写入失败：{message}（返回码 0x{info.ReturnCode:X2}），"
                    + $"{DescribeWriteChunk(request, request.StartByte, 1)}，位号 {bitNumber}。",
                    plcReturnCode: info.ReturnCode);
            }

            LogChunk(
                scope,
                "Write",
                S7OperationStage.Write,
                request.Area,
                request.DbNumber,
                request.StartByte,
                length: 1,
                chunkIndex: 1,
                chunkCount: 1);
            Log(
                scope,
                "Write",
                S7OperationStage.Write,
                succeeded: true,
                request.Area,
                request.DbNumber,
                request.StartByte,
                length: 1,
                chunkCount: 1);
            return 1;
        }
        catch (OperationCanceledException ex)
        {
            bool uncertain = writeIssued;
            if (!timeout.CallerTriggered && _reconnect.ReconnectOnTimeout)
            {
                await HandleFaultAsync(
                        transport,
                        S7OperationStage.Write,
                        S7ErrorCode.WriteTimeout,
                        S7ErrorCategory.Timeout,
                        callerCancelled: false)
                    .ConfigureAwait(false);
            }
            else if (!timeout.CallerTriggered)
            {
                await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
            }
            else
            {
                bool drained = await DrainAsync(transport, pduReference, S7OperationStage.Write).ConfigureAwait(false);
                if (!drained)
                {
                    await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                }
            }

            S7ClientException mapped = MapCanceled(
                timeout,
                S7OperationStage.Write,
                S7ErrorCode.WriteTimeout,
                DescribeWriteChunk(request, request.StartByte, 1),
                ex,
                bytesWritten: 0,
                writtenRange: null,
                writeOutcomeUncertain: uncertain);
            LogFailure(
                scope,
                "Write",
                S7OperationStage.Write,
                mapped,
                request.Area,
                request.DbNumber,
                request.StartByte,
                length: 1);
            throw mapped;
        }
        catch (TransportException ex)
        {
            await HandleFaultAsync(
                    transport,
                    S7OperationStage.Write,
                    S7ErrorCode.ConnectionReset,
                    S7ErrorCategory.Connection,
                    callerCancelled: false)
                .ConfigureAwait(false);

            var mapped = new S7ClientException(
                S7ErrorCategory.Connection,
                S7ErrorCode.ConnectionReset,
                S7OperationStage.Write,
                $"位写入过程中连接中断：{DescribeWriteChunk(request, request.StartByte, 1)}，位号 {bitNumber}。",
                bytesWritten: 0,
                writtenRange: null,
                writeOutcomeUncertain: writeIssued,
                innerException: ex);
            LogFailure(
                scope,
                "Write",
                S7OperationStage.Write,
                mapped,
                request.Area,
                request.DbNumber,
                request.StartByte,
                length: 1);
            throw mapped;
        }
        catch (S7ClientException ex)
        {
            LogFailure(
                scope,
                "Write",
                S7OperationStage.Write,
                ex,
                request.Area,
                request.DbNumber,
                request.StartByte,
                length: 1);
            throw;
        }
    }

    internal async Task<int> WriteAsync(S7WriteRequest request, CancellationToken cancellationToken)
    {
        S7WriteResult result = await WriteResultAsync(request, cancellationToken).ConfigureAwait(false);
        return result.BytesWritten;
    }

    internal async Task<S7WriteResult> WriteResultAsync(S7WriteRequest request, CancellationToken cancellationToken)
    {
        int recovery = 0;
        int acknowledged = 0;
        bool replayAttempted = false;
        int replayStart = 0;

        while (true)
        {
            try
            {
                int written = await WriteOnceAsync(request, acknowledged, recovery == 0, cancellationToken)
                    .ConfigureAwait(false);
                return new S7WriteResult(
                    written,
                    recovery,
                    recovery > 0,
                    replayAttempted,
                    replayAttempted ? written - replayStart : 0);
            }
            catch (S7ClientException exception) when (recovery == 0 && ShouldRecover(exception))
            {
                if (exception.WriteOutcomeUncertain && !_reconnect.ReplayUncertainWrites)
                {
                    throw;
                }

                replayAttempted = exception.WriteOutcomeUncertain;
                replayStart = exception.BytesWritten;
                acknowledged = exception.BytesWritten;
                await WaitForRecoveryAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
                recovery = 1;
            }
        }
    }

    private async Task<int> WriteOnceAsync(
        S7WriteRequest request,
        int startOffset,
        bool allowWait,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Write, allowWait, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, request.Data.Length),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Write, allowWait: false, cancellationToken).ConfigureAwait(false);

        int payloadLimit = NegotiatedPduSize - ProtocolConstants.WriteRequestOverhead;
        int totalLength = request.Data.Length;
        int chunkCount = (totalLength + payloadLimit - 1) / payloadLimit;
        int offset = startOffset;
        int chunkIndex = startOffset / payloadLimit;
        var scope = new OperationScope();
        ITcpTransport transport = _transport!;

        while (offset < totalLength)
        {
            chunkIndex++;
            int chunkLength = Math.Min(payloadLimit, totalLength - offset);
            int chunkStart = request.StartByte + offset;
            ReadOnlySpan<byte> chunk = request.Data.Span.Slice(offset, chunkLength);
            bool writeIssued = false;

            using var timeout = new OperationTimeout(_options.WriteTimeout, cancellationToken, _timeProvider);
            ushort pduReference = _references.Next();
            byte[] frame = S7RequestBuilder.BuildWriteRequest(
                request.Area,
                request.DbNumber,
                chunkStart,
                chunk,
                pduReference);

            try
            {
                TraceFrame("Write", S7OperationStage.Write, frame);
                await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
                writeIssued = true;
                byte[] payload = await S7FrameReader
                    .ReadS7PayloadAsync(transport, S7OperationStage.Write, timeout.Token)
                    .ConfigureAwait(false);

                WriteResponseInfo info = S7ResponseParser.ParseWriteResponse(payload, pduReference);
                if (info.ReturnCode != ProtocolConstants.SuccessReturnCode)
                {
                    WrittenRange? failedRange = offset > 0
                        ? new WrittenRange(request.StartByte, offset)
                        : null;

                    throw new S7ClientException(
                        S7ErrorCategory.PlcReturn,
                        PlcReturnCodeMapper.Map(info.ReturnCode).Code,
                        S7OperationStage.Write,
                        $"写入失败：{PlcReturnCodeMapper.Map(info.ReturnCode).Message}"
                        + $"（返回码 0x{info.ReturnCode:X2}），{DescribeWriteChunk(request, chunkStart, chunkLength)}。",
                        plcReturnCode: info.ReturnCode,
                        bytesWritten: offset,
                        writtenRange: failedRange);
                }

                offset += chunkLength;
                LogChunk(
                    scope,
                    "Write",
                    S7OperationStage.Write,
                    request.Area,
                    request.DbNumber,
                    chunkStart,
                    chunkLength,
                    chunkIndex,
                    chunkCount);
            }
            catch (OperationCanceledException ex)
            {
                bool uncertain = writeIssued;
                WrittenRange? writtenRange = offset > 0
                    ? new WrittenRange(request.StartByte, offset)
                    : null;

                if (!timeout.CallerTriggered && _reconnect.ReconnectOnTimeout)
                {
                    await HandleFaultAsync(
                            transport,
                            S7OperationStage.Write,
                            S7ErrorCode.WriteTimeout,
                            S7ErrorCategory.Timeout,
                            callerCancelled: false)
                        .ConfigureAwait(false);
                }
                else if (!timeout.CallerTriggered)
                {
                    await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                }
                else
                {
                    bool drained = await DrainAsync(transport, pduReference, S7OperationStage.Write)
                        .ConfigureAwait(false);
                    if (!drained)
                    {
                        await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                    }
                }

                S7ClientException mapped = MapCanceled(
                    timeout,
                    S7OperationStage.Write,
                    S7ErrorCode.WriteTimeout,
                    DescribeWriteChunk(request, chunkStart, chunkLength),
                    ex,
                    bytesWritten: offset,
                    writtenRange: writtenRange,
                    writeOutcomeUncertain: uncertain);
                LogFailure(
                    scope,
                    "Write",
                    S7OperationStage.Write,
                    mapped,
                    request.Area,
                    request.DbNumber,
                    chunkStart,
                    chunkLength);
                throw mapped;
            }
            catch (TransportException ex)
            {
                bool uncertain = writeIssued;
                WrittenRange? writtenRange = offset > 0
                    ? new WrittenRange(request.StartByte, offset)
                    : null;

                await HandleFaultAsync(
                        transport,
                        S7OperationStage.Write,
                        S7ErrorCode.ConnectionReset,
                        S7ErrorCategory.Connection,
                        callerCancelled: false)
                    .ConfigureAwait(false);

                var mapped = new S7ClientException(
                    S7ErrorCategory.Connection,
                    S7ErrorCode.ConnectionReset,
                    S7OperationStage.Write,
                    $"写入过程中连接中断：{DescribeWriteChunk(request, chunkStart, chunkLength)}。",
                    bytesWritten: offset,
                    writtenRange: writtenRange,
                    writeOutcomeUncertain: uncertain,
                    innerException: ex);
                LogFailure(
                    scope,
                    "Write",
                    S7OperationStage.Write,
                    mapped,
                    request.Area,
                    request.DbNumber,
                    chunkStart,
                    chunkLength);
                throw mapped;
            }
            catch (S7ClientException ex)
            {
                LogFailure(
                    scope,
                    "Write",
                    S7OperationStage.Write,
                    ex,
                    request.Area,
                    request.DbNumber,
                    chunkStart,
                    chunkLength);
                throw;
            }
        }

        Log(
            scope,
            "Write",
            S7OperationStage.Write,
            succeeded: true,
            request.Area,
            request.DbNumber,
            request.StartByte,
            totalLength,
            chunkCount: chunkCount);
        return offset;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (State == S7SessionState.Reconnecting)
        {
            await AbortReconnectAsync(S7ReconnectAbortReason.ClientDisposed).ConfigureAwait(false);
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task EnsureConnectedAsync(
        S7OperationStage stage,
        bool allowWait,
        CancellationToken cancellationToken)
    {
        bool waited = false;

        while (true)
        {
            S7SessionState state = State;
            if (state == S7SessionState.Connected)
            {
                return;
            }

            if (state == S7SessionState.Reconnecting)
            {
                if (!allowWait || waited || _reconnect.WaitTimeout <= TimeSpan.Zero)
                {
                    throw new S7ClientException(
                        S7ErrorCategory.InvalidArgument,
                        S7ErrorCode.SessionReconnecting,
                        stage,
                        "会话正在自动重连，请求等待窗口为零或已用尽。");
                }

                TaskCompletionSource<ReconnectOutcome>? signal;
                lock (_sync)
                {
                    signal = _reconnectSignal;
                }

                if (signal is null)
                {
                    throw new S7ClientException(
                        S7ErrorCategory.InvalidArgument,
                        S7ErrorCode.SessionReconnecting,
                        stage,
                        "会话正在自动重连，未取得重连终态信号。");
                }

                using var timeout = new OperationTimeout(_reconnect.WaitTimeout, cancellationToken, _timeProvider);
                ReconnectOutcome outcome;
                try
                {
                    outcome = await signal.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    if (timeout.CallerTriggered)
                    {
                        throw new S7ClientException(
                            S7ErrorCategory.Cancelled,
                            S7ErrorCode.OperationCancelled,
                            stage,
                            "等待自动重连期间操作已取消。",
                            innerException: ex);
                    }

                    throw new S7ClientException(
                        S7ErrorCategory.InvalidArgument,
                        S7ErrorCode.SessionReconnecting,
                        stage,
                        $"等待自动重连超时（窗口 {_reconnect.WaitTimeout.TotalSeconds} 秒）。",
                        innerException: ex);
                }

                waited = true;
                switch (outcome)
                {
                    case ReconnectOutcome.Reconnected:
                        continue;
                    case ReconnectOutcome.Exhausted:
                        throw new S7ClientException(
                            S7ErrorCategory.InvalidArgument,
                            S7ErrorCode.ReconnectExhausted,
                            S7OperationStage.Reconnect,
                            "自动重连尝试次数已耗尽，会话已断开。");
                    default:
                        throw new S7ClientException(
                            S7ErrorCategory.InvalidArgument,
                            S7ErrorCode.ReconnectAborted,
                            S7OperationStage.Reconnect,
                            "自动重连已被中止。");
                }
            }

            if (state == S7SessionState.Faulted)
            {
                throw new S7ClientException(
                    S7ErrorCategory.InvalidArgument,
                    S7ErrorCode.SessionFaulted,
                    stage,
                    "会话处于异常状态，必须显式断开后重连。");
            }

            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionNotEstablished,
                stage,
                "会话未建立，禁止执行读写请求。");
        }
    }

    private async Task WaitForRecoveryAsync(S7OperationStage stage, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(stage, allowWait: true, cancellationToken).ConfigureAwait(false);
    }

    private bool ShouldRecover(S7ClientException exception)
    {
        if (!_reconnect.AutoReconnectEnabled || State != S7SessionState.Reconnecting)
        {
            return false;
        }

        return exception.Category == S7ErrorCategory.Connection
            || (exception.Category == S7ErrorCategory.Timeout && _reconnect.ReconnectOnTimeout);
    }

    private async Task<PduNegotiation> HandshakeAsync(ITcpTransport transport, CancellationToken cancellationToken)
    {
        S7OperationStage stage = S7OperationStage.Connect;
        using var timeout = new OperationTimeout(_options.ConnectTimeout, cancellationToken, _timeProvider);

        try
        {
            await transport.ConnectAsync(_options.IpAddress, _options.Port, _options.ConnectTimeout, timeout.Token)
                .ConfigureAwait(false);

            stage = S7OperationStage.CotpConnect;
            (Tsap local, Tsap remote) = TsapResolver.Resolve(_options);
            byte[] connectionRequest = CotpCodec.BuildConnectionRequest(local, remote, stage);
            TraceFrame("Connect", stage, connectionRequest);
            await transport.WriteAsync(connectionRequest, timeout.Token).ConfigureAwait(false);

            byte[] connectionConfirm = await S7FrameReader.ReadFrameAsync(transport, stage, timeout.Token)
                .ConfigureAwait(false);

            if (!CotpCodec.IsConnectionConfirm(connectionConfirm, stage))
            {
                throw new S7ClientException(
                    S7ErrorCategory.Connection,
                    S7ErrorCode.CotpConnectionRejected,
                    stage,
                    $"PLC 拒绝了 COTP 连接请求（目标 {_options.IpAddress}:{_options.Port}）。");
            }

            stage = S7OperationStage.S7Setup;
            ushort pduReference = _references.Next();
            byte[] setupRequest = S7RequestBuilder.BuildSetupCommunication(_options.RequestedPduSize, pduReference);
            TraceFrame("Connect", stage, setupRequest);
            await transport.WriteAsync(setupRequest, timeout.Token).ConfigureAwait(false);

            byte[] setupResponse = await S7FrameReader.ReadS7PayloadAsync(transport, stage, timeout.Token)
                .ConfigureAwait(false);
            int confirmedPduSize = S7ResponseParser.ParseSetupResponse(setupResponse, pduReference);
            return PduSizeNegotiator.Negotiate(_options.RequestedPduSize, confirmedPduSize);
        }
        catch (OperationCanceledException ex)
        {
            throw MapCanceled(
                timeout,
                stage,
                S7ErrorCode.ConnectTimeout,
                $"连接 {_options.IpAddress}:{_options.Port}（阶段 {stage}）",
                ex);
        }
        catch (TransportException ex)
        {
            throw MapTransport(ex, stage);
        }
        catch (S7ClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.InvalidFrame,
                stage,
                $"连接过程发生未预期错误（阶段 {stage}）：{ex.Message}",
                innerException: ex);
        }
    }

    private ITcpTransport CreateTransport()
    {
        if (_transportFactory is not null)
        {
            return _transportFactory();
        }

        return _injectedTransport ?? new TcpTransport();
    }

    private bool StartReconnect(S7ErrorCode firstFault)
    {
        if (!TryTransition(
                S7SessionState.Connected,
                S7SessionState.Reconnecting,
                0,
                firstFault,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _))
        {
            return false;
        }

        var cancellation = new CancellationTokenSource();
        var signal = new TaskCompletionSource<ReconnectOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task task = Task.Run(() => RunReconnectLoopAsync(firstFault, cancellation.Token));
        lock (_sync)
        {
            _reconnectCancellation = cancellation;
            _reconnectSignal = signal;
            _reconnectTask = task;
            _reconnectAttempt = 0;
            _nextReconnectDelay = null;
            _firstFaultErrorCode = firstFault;
            _reconnectAbortReason = S7ReconnectAbortReason.DisconnectedByCaller;
        }

        return true;
    }

    private async Task RunReconnectLoopAsync(S7ErrorCode firstFault, CancellationToken cancellationToken)
    {
        DateTimeOffset disconnectedAt = _timeProvider.GetUtcNow();
        int attempt = 0;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                FinishAborted(_reconnectAbortReason);
                return;
            }

            attempt++;
            TimeSpan delay = BackoffDelayCalculator.Calculate(attempt, _reconnect);
            lock (_sync)
            {
                _reconnectAttempt = attempt;
                _nextReconnectDelay = delay;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    FinishAborted(_reconnectAbortReason);
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                FinishAborted(_reconnectAbortReason);
                return;
            }

            DateTimeOffset attemptStartedAt = _timeProvider.GetUtcNow();
            RaiseReconnectEvent(new S7ReconnectEventArgs
            {
                Kind = S7ReconnectEventKind.AttemptStarted,
                Attempt = attempt,
                MaxAttempts = _reconnect.MaxAttempts,
                Delay = delay,
                Elapsed = attemptStartedAt - disconnectedAt
            });
            LogReconnect(
                S7LogLevel.Information,
                succeeded: true,
                attempt,
                delay,
                disconnectedDuration: null);

            ITcpTransport? transport = null;
            try
            {
                transport = CreateTransport();
                PduNegotiation negotiation;
                using (RequestGate.Lease lease = await _gate
                           .AcquireAsync(S7OperationStage.Reconnect, cancellationToken)
                           .ConfigureAwait(false))
                {
                    negotiation = await HandshakeAsync(transport, cancellationToken).ConfigureAwait(false);
                }

                if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                    FinishAborted(_reconnectAbortReason);
                    return;
                }

                _transport = transport;
                Guid newSessionId = Guid.NewGuid();
                DateTimeOffset connectedAt = _timeProvider.GetUtcNow();
                if (!TryTransition(
                        S7SessionState.Reconnecting,
                        S7SessionState.Connected,
                        0,
                        null,
                        newSessionId,
                        negotiation.Effective,
                        connectedAt,
                        null,
                        out _))
                {
                    _transport = null;
                    await transport.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                lock (_sync)
                {
                    _reconnectAttempt = 0;
                    _nextReconnectDelay = null;
                    _firstFaultErrorCode = null;
                }

                RaiseReconnectEvent(new S7ReconnectEventArgs
                {
                    Kind = S7ReconnectEventKind.Reconnected,
                    Attempt = attempt,
                    MaxAttempts = _reconnect.MaxAttempts,
                    Delay = delay,
                    Elapsed = connectedAt - disconnectedAt,
                    DisconnectedDuration = connectedAt - disconnectedAt,
                    NewSessionId = newSessionId,
                    NegotiatedPduSize = negotiation.Effective
                });
                LogReconnect(
                    S7LogLevel.Information,
                    succeeded: true,
                    attempt,
                    delay,
                    connectedAt - disconnectedAt,
                    negotiatedPduSize: negotiation.Effective);
                _reconnectSignal?.TrySetResult(ReconnectOutcome.Reconnected);
                return;
            }
            catch (OperationCanceledException)
            {
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                FinishAborted(_reconnectAbortReason);
                return;
            }
            catch (S7ClientException exception)
            {
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                DateTimeOffset failedAt = _timeProvider.GetUtcNow();
                RaiseReconnectEvent(new S7ReconnectEventArgs
                {
                    Kind = S7ReconnectEventKind.AttemptFailed,
                    Attempt = attempt,
                    MaxAttempts = _reconnect.MaxAttempts,
                    Delay = delay,
                    Elapsed = failedAt - disconnectedAt,
                    ErrorCategory = exception.Category,
                    ErrorCode = exception.ErrorCode
                });
                LogReconnect(
                    S7LogLevel.Warning,
                    succeeded: false,
                    attempt,
                    delay,
                    disconnectedDuration: null,
                    errorCode: exception.ErrorCode,
                    exception: exception);
            }
            catch (Exception exception)
            {
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    FinishAborted(S7ReconnectAbortReason.ClientDisposed);
                    return;
                }

                DateTimeOffset failedAt = _timeProvider.GetUtcNow();
                RaiseReconnectEvent(new S7ReconnectEventArgs
                {
                    Kind = S7ReconnectEventKind.AttemptFailed,
                    Attempt = attempt,
                    MaxAttempts = _reconnect.MaxAttempts,
                    Delay = delay,
                    Elapsed = failedAt - disconnectedAt,
                    ErrorCategory = S7ErrorCategory.Connection,
                    ErrorCode = S7ErrorCode.ConnectionReset
                });
                LogReconnect(
                    S7LogLevel.Warning,
                    succeeded: false,
                    attempt,
                    delay,
                    disconnectedDuration: null,
                    errorCode: S7ErrorCode.ConnectionReset,
                    exception: exception);
            }

            if (_reconnect.MaxAttempts > 0 && attempt >= _reconnect.MaxAttempts)
            {
                FinishExhausted(firstFault, attempt, delay, disconnectedAt);
                return;
            }
        }
    }

    private void FinishExhausted(
        S7ErrorCode firstFault,
        int attempt,
        TimeSpan delay,
        DateTimeOffset disconnectedAt)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!TryTransition(
                S7SessionState.Reconnecting,
                S7SessionState.Disconnected,
                0,
                firstFault,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _))
        {
            return;
        }

        RaiseReconnectEvent(new S7ReconnectEventArgs
        {
            Kind = S7ReconnectEventKind.Exhausted,
            Attempt = attempt,
            MaxAttempts = _reconnect.MaxAttempts,
            Delay = delay,
            Elapsed = now - disconnectedAt,
            ErrorCategory = S7ErrorCategory.Connection,
            ErrorCode = firstFault,
            DisconnectedDuration = now - disconnectedAt
        });
        LogReconnect(
            S7LogLevel.Error,
            succeeded: false,
            attempt,
            delay,
            now - disconnectedAt,
            errorCode: firstFault);
        _reconnectSignal?.TrySetResult(ReconnectOutcome.Exhausted);
    }

    private void FinishAborted(S7ReconnectAbortReason reason)
    {
        if (!TryTransition(
                S7SessionState.Reconnecting,
                S7SessionState.Disconnected,
                0,
                _firstFaultErrorCode,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _))
        {
            return;
        }

        RaiseReconnectEvent(new S7ReconnectEventArgs
        {
            Kind = S7ReconnectEventKind.Aborted,
            Attempt = Math.Max(1, _reconnectAttempt),
            MaxAttempts = _reconnect.MaxAttempts,
            Delay = TimeSpan.Zero,
            AbortReason = reason
        });
        LogReconnect(
            S7LogLevel.Error,
            succeeded: false,
            _reconnectAttempt,
            TimeSpan.Zero,
            disconnectedDuration: null,
            errorCode: S7ErrorCode.ReconnectAborted);
        _reconnectSignal?.TrySetResult(ReconnectOutcome.Aborted);
    }

    private async Task AbortReconnectAsync(S7ReconnectAbortReason reason)
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_sync)
        {
            _reconnectAbortReason = reason;
            cancellation = _reconnectCancellation;
            task = _reconnectTask;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (task is not null && task.Id != Task.CurrentId)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (Exception)
            {
            }
        }

        FinishAborted(reason);
        CleanupReconnectArtifacts();
    }

    private void CleanupReconnectArtifacts()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            cancellation = _reconnectCancellation;
            _reconnectCancellation = null;
            _reconnectTask = null;
            _reconnectSignal = null;
            _reconnectAttempt = 0;
            _nextReconnectDelay = null;
        }

        cancellation?.Dispose();
    }

    private async Task<bool> HandleFaultAsync(
        ITcpTransport transport,
        S7OperationStage stage,
        S7ErrorCode errorCode,
        S7ErrorCategory category,
        bool callerCancelled)
    {
        S7SessionState stateBefore = State;
        _transport = null;
        await transport.DisposeAsync().ConfigureAwait(false);

        if (ReconnectFaultClassifier.ShouldReconnect(_reconnect, stateBefore, callerCancelled, category, stage)
            && StartReconnect(errorCode))
        {
            return true;
        }

        if (stateBefore == S7SessionState.Connected)
        {
            TryTransition(
                S7SessionState.Connected,
                S7SessionState.Faulted,
                0,
                null,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _);
        }

        return false;
    }

    private async ValueTask FailConnectAsync(ITcpTransport transport)
    {
        _transport = null;
        Volatile.Write(ref _negotiatedPdu, 0);
        _sessionId = Guid.Empty;
        await transport.DisposeAsync().ConfigureAwait(false);
        TryTransition(
            S7SessionState.Connecting,
            S7SessionState.Disconnected,
            0,
            null,
            Guid.Empty,
            0,
            _lastReconnectAt,
            null,
            out _);
    }

    private async ValueTask<bool> DrainAsync(
        ITcpTransport transport,
        ushort pduReference,
        S7OperationStage stage)
    {
        TimeSpan configured = stage == S7OperationStage.Read ? _options.ReadTimeout : _options.WriteTimeout;
        TimeSpan drainTimeout = configured < TimeSpan.FromSeconds(2) ? configured : TimeSpan.FromSeconds(2);

        return await LateResponseDrainer
            .TryDrainAsync(transport, pduReference, drainTimeout, stage)
            .ConfigureAwait(false);
    }

    private async ValueTask CloseTransportAfterFaultAsync(ITcpTransport transport)
    {
        _transport = null;
        Volatile.Write(ref _negotiatedPdu, 0);
        await transport.DisposeAsync().ConfigureAwait(false);

        if (State == S7SessionState.Connected)
        {
            TryTransition(
                S7SessionState.Connected,
                S7SessionState.Faulted,
                0,
                null,
                Guid.Empty,
                0,
                _lastReconnectAt,
                null,
                out _);
        }
    }

    private bool TryTransition(
        S7SessionState expected,
        S7SessionState target,
        int attempt,
        S7ErrorCode? firstFault,
        Guid sessionId,
        int pdu,
        DateTimeOffset? lastReconnectAt,
        TimeSpan? nextDelay,
        out SessionSnapshot snapshot)
    {
        S7SessionState previous;
        lock (_sync)
        {
            previous = (S7SessionState)_state;
            if (previous != expected)
            {
                snapshot = CreateSnapshotLocked();
                return false;
            }

            _state = (int)target;
            _reconnectAttempt = attempt;
            _firstFaultErrorCode = firstFault;
            _sessionId = sessionId;
            _negotiatedPdu = pdu;
            _lastReconnectAt = lastReconnectAt;
            _nextReconnectDelay = nextDelay;
            snapshot = CreateSnapshotLocked();
        }

        RaiseStateChanged(previous, target, snapshot);
        return true;
    }

    private SessionSnapshot CreateSnapshotLocked()
    {
        return new SessionSnapshot
        {
            State = (S7SessionState)_state,
            SessionId = _sessionId,
            NegotiatedPduSize = _negotiatedPdu,
            AutoReconnectEnabled = _reconnect.AutoReconnectEnabled,
            ReconnectAttempt = _reconnectAttempt,
            LastReconnectAt = _lastReconnectAt,
            NextReconnectDelay = _nextReconnectDelay
        };
    }

    private void RaiseStateChanged(S7SessionState previous, S7SessionState current, SessionSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        EventHandler<S7SessionStateChangedEventArgs>? handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        var args = new S7SessionStateChangedEventArgs
        {
            PreviousState = previous,
            CurrentState = current,
            SessionId = snapshot.SessionId,
            Attempt = current == S7SessionState.Reconnecting ? snapshot.ReconnectAttempt : null,
            FirstFaultErrorCode = current == S7SessionState.Reconnecting ? _firstFaultErrorCode : null,
            Timestamp = _timeProvider.GetUtcNow()
        };

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<S7SessionStateChangedEventArgs>)handler)(this, args);
            }
            catch
            {
            }
        }
    }

    private void RaiseReconnectEvent(S7ReconnectEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        EventHandler<S7ReconnectEventArgs>? handlers = ReconnectEventRaised;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<S7ReconnectEventArgs>)handler)(this, args);
            }
            catch
            {
            }
        }
    }

    private async ValueTask<RequestGate.Lease> AcquireGateAsync(
        S7OperationStage stage,
        string context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gate.AcquireAsync(stage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new S7ClientException(
                S7ErrorCategory.Cancelled,
                S7ErrorCode.OperationCancelled,
                stage,
                $"操作已取消：{context}。",
                innerException: ex);
        }
    }

    private S7ClientException MapCanceled(
        OperationTimeout timeout,
        S7OperationStage stage,
        S7ErrorCode timeoutCode,
        string context,
        Exception inner,
        int bytesWritten = 0,
        WrittenRange? writtenRange = null,
        bool writeOutcomeUncertain = false)
    {
        if (timeout.CallerTriggered)
        {
            return new S7ClientException(
                S7ErrorCategory.Cancelled,
                S7ErrorCode.OperationCancelled,
                stage,
                $"操作已取消：{context}。",
                bytesWritten: bytesWritten,
                writtenRange: writtenRange,
                writeOutcomeUncertain: writeOutcomeUncertain,
                innerException: inner);
        }

        TimeSpan timeoutValue = stage switch
        {
            S7OperationStage.Read => _options.ReadTimeout,
            S7OperationStage.Write => _options.WriteTimeout,
            _ => _options.ConnectTimeout
        };

        return new S7ClientException(
            S7ErrorCategory.Timeout,
            timeoutCode,
            stage,
            $"操作超时：{context}（超时 {timeoutValue.TotalSeconds} 秒）。",
            bytesWritten: bytesWritten,
            writtenRange: writtenRange,
            writeOutcomeUncertain: writeOutcomeUncertain,
            innerException: inner);
    }

    private static S7ClientException MapTransport(TransportException exception, S7OperationStage stage)
    {
        if (exception.Code == S7ErrorCode.ConnectTimeout)
        {
            return new S7ClientException(
                S7ErrorCategory.Timeout,
                S7ErrorCode.ConnectTimeout,
                stage,
                exception.Message,
                innerException: exception);
        }

        S7ErrorCode code = stage == S7OperationStage.Connect
            ? S7ErrorCode.TcpConnectFailed
            : S7ErrorCode.ConnectionReset;

        return new S7ClientException(
            S7ErrorCategory.Connection,
            code,
            stage,
            exception.Message,
            innerException: exception);
    }

    private static S7ClientException CreatePlcException(
        byte returnCode,
        S7OperationStage stage,
        string context)
    {
        (S7ErrorCode code, string message) = PlcReturnCodeMapper.Map(returnCode);

        return new S7ClientException(
            S7ErrorCategory.PlcReturn,
            code,
            stage,
            $"PLC 返回错误：{message}（返回码 0x{returnCode:X2}），{context}。",
            plcReturnCode: returnCode);
    }

    private static string DescribeReadChunk(S7ReadRequest request, int startByte, int length)
    {
        return $"区域 {request.Area}，DB {request.DbNumber}，起始 {startByte}，长度 {length}";
    }

    private static string DescribeWriteChunk(S7WriteRequest request, int startByte, int length)
    {
        return $"区域 {request.Area}，DB {request.DbNumber}，起始 {startByte}，长度 {length}";
    }

    private void TraceFrame(string operation, S7OperationStage stage, ReadOnlySpan<byte> frame)
    {
        if (!_options.EnableProtocolTrace || !_logger.IsEnabled(S7LogLevel.Trace))
        {
            return;
        }

        Log(
            new OperationScope(),
            operation,
            stage,
            succeeded: true,
            protocolTrace: Convert.ToHexString(frame));
    }

    private void LogChunk(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        S7Area area,
        int dbNumber,
        int startByte,
        int length,
        int chunkIndex,
        int chunkCount)
    {
        if (!_logger.IsEnabled(S7LogLevel.Debug))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            Area = area,
            DbNumber = dbNumber,
            StartByte = startByte,
            Length = length,
            ChunkIndex = chunkIndex,
            ChunkCount = chunkCount,
            Elapsed = scope.Elapsed,
            Succeeded = true
        };

        _logger.Log(S7LogLevel.Debug, in entry);
    }

    private void LogFailure(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        S7ClientException exception,
        S7Area? area = null,
        int? dbNumber = null,
        int? startByte = null,
        int? length = null)
    {
        Log(
            scope,
            operation,
            stage,
            succeeded: false,
            area,
            dbNumber,
            startByte,
            length,
            errorCode: exception.ErrorCode,
            plcReturnCode: exception.PlcReturnCode,
            exception: exception);
    }

    private void LogReconnect(
        S7LogLevel level,
        bool succeeded,
        int attempt,
        TimeSpan delay,
        TimeSpan? disconnectedDuration,
        S7ErrorCode? errorCode = null,
        int? negotiatedPduSize = null,
        Exception? exception = null)
    {
        Log(
            new OperationScope(),
            "Reconnect",
            S7OperationStage.Reconnect,
            succeeded,
            errorCode: errorCode,
            negotiatedPduSize: negotiatedPduSize,
            attempt: attempt,
            maxAttempts: _reconnect.MaxAttempts,
            reconnectDelay: delay,
            disconnectedDuration: disconnectedDuration,
            exception: exception,
            level: level);
    }

    private void Log(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        bool succeeded,
        S7Area? area = null,
        int? dbNumber = null,
        int? startByte = null,
        int? length = null,
        int? chunkIndex = null,
        int? chunkCount = null,
        int? negotiatedPduSize = null,
        S7ErrorCode? errorCode = null,
        byte? plcReturnCode = null,
        string? protocolTrace = null,
        Exception? exception = null,
        int? attempt = null,
        int? maxAttempts = null,
        TimeSpan? reconnectDelay = null,
        TimeSpan? disconnectedDuration = null,
        S7LogLevel? level = null)
    {
        S7LogLevel effectiveLevel = level
            ?? (protocolTrace is null
                ? (succeeded ? S7LogLevel.Information : S7LogLevel.Warning)
                : S7LogLevel.Trace);

        if (!_logger.IsEnabled(effectiveLevel))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            Area = area,
            DbNumber = dbNumber,
            StartByte = startByte,
            Length = length,
            ChunkIndex = chunkIndex,
            ChunkCount = chunkCount,
            Elapsed = scope.Elapsed,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            PlcReturnCode = plcReturnCode,
            NegotiatedPduSize = negotiatedPduSize,
            ProtocolTrace = protocolTrace,
            Attempt = attempt,
            MaxAttempts = maxAttempts,
            ReconnectDelay = reconnectDelay,
            DisconnectedDuration = disconnectedDuration
        };

        _logger.Log(effectiveLevel, in entry, exception);
    }
}