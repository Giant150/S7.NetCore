using System.Buffers;
using System.Diagnostics;

namespace S7.NetCore.Internal;

/// <summary>
/// S7 会话：状态机、超时编排、分包读写、结果装配与可选断线自动重连。
/// </summary>
internal sealed partial class S7Session : IAsyncDisposable
{
    private readonly S7ConnectionOptions _options;
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
    private CancellationTokenSource? _reconnectCancellation;
    private Task? _reconnectTask;
    private CancellationTokenSource? _connectionLostCts;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly CancellationToken _shutdownToken;
    private readonly SideEffectPump _sideEffects;
    private readonly AftercareQueue _aftercare = new();
    private long _connectionEpoch;

    internal S7Session(
        S7ConnectionOptions options,
        IS7Logger? logger,
        ITcpTransport? transport,
        TimeProvider? timeProvider,
        Func<ITcpTransport>? transportFactory = null)
    {
        _options = options;
        _logger = logger is null or NullS7Logger ? NullS7Logger.Instance : new SafeS7Logger(logger);
        _injectedTransport = transport;
        _transportFactory = transportFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _gate = new RequestGate(options.MaxQueuedRequests);
        _shutdownToken = _shutdownCts.Token;
        _sideEffects = new SideEffectPump(_logger, DispatchStateChanged, _timeProvider);
    }

    internal event EventHandler<S7SessionStateChangedEventArgs>? StateChanged;

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
                Guid.Empty,
                0,
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
                    Guid.NewGuid(),
                    negotiation.Effective,
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
        _aftercare.Clear();

        if (State == S7SessionState.Disconnected)
        {
            CleanupReconnectArtifacts();
            return;
        }

        var scope = new OperationScope();

        if (State == S7SessionState.Reconnecting)
        {
            await AbortReconnectAsync().ConfigureAwait(false);

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
                Guid.Empty,
                0,
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

    internal Task<ReadOnlyMemory<byte>> ReadAsync(S7ReadRequest request, CancellationToken cancellationToken)
    {
        return ReadOnceAsync(request, cancellationToken);
    }

    private async Task<ReadOnlyMemory<byte>> ReadOnceAsync(
        S7ReadRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Read, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Read,
                DescribeReadChunk(request, request.StartByte, request.Length),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Read, cancellationToken).ConfigureAwait(false);
        ITcpTransport transport = _transport!;
        await DrainPendingAsync(transport, Volatile.Read(ref _connectionEpoch), cancellationToken)
            .ConfigureAwait(false);

        int payloadLimit = NegotiatedPduSize - ProtocolConstants.ReadResponseOverhead;
        int totalLength = request.Length;
        int chunkCount = (totalLength + payloadLimit - 1) / payloadLimit;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        var scope = new OperationScope();
        int offset = 0;
        int chunkIndex = 0;

        try
        {
            while (offset < totalLength)
            {
                ThrowIfCannotSend(S7OperationStage.Read, cancellationToken);
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

                bool requestIssued = false;
                try
                {
                    TraceFrame("Read", S7OperationStage.Read, frame);
                    await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
                    requestIssued = true;
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
                    long judgedAt = Stopwatch.GetTimestamp();
                    if (requestIssued)
                    {
                        PostAftercare(transport, pduReference, S7OperationStage.Read);
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
                        chunkLength,
                        cause: CreateCancelDecision(
                            timeout,
                            requestIssued,
                            Stopwatch.GetElapsedTime(judgedAt),
                            DescribeReadChunk(request, chunkStart, chunkLength)));
                    throw mapped;
                }
                catch (TransportException ex)
                {
                    long faultStart = Stopwatch.GetTimestamp();
                    bool reconnectStarted = HandleFault(transport);
                    TimeSpan faultElapsed = Stopwatch.GetElapsedTime(faultStart);

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
                        chunkLength,
                        cause: CreateFaultDecision(
                            reconnectStarted,
                            faultElapsed,
                            mapped,
                            aftercare: DescribeFaultAftercare(reconnectStarted)));
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

    internal Task<int> WriteBitAsync(
        S7WriteRequest request,
        int bitNumber,
        bool value,
        CancellationToken cancellationToken)
    {
        return WriteBitOnceAsync(request, bitNumber, value, cancellationToken);
    }

    private async Task<int> WriteBitOnceAsync(
        S7WriteRequest request,
        int bitNumber,
        bool value,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, 1),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        ITcpTransport transport = _transport!;
        await DrainPendingAsync(transport, Volatile.Read(ref _connectionEpoch), cancellationToken)
            .ConfigureAwait(false);
        ThrowIfCannotSend(S7OperationStage.Write, cancellationToken);

        var scope = new OperationScope();
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
            long judgedAt = Stopwatch.GetTimestamp();
            bool uncertain = writeIssued;
            if (writeIssued)
            {
                PostAftercare(transport, pduReference, S7OperationStage.Write);
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
                length: 1,
                cause: CreateCancelDecision(
                    timeout,
                    writeIssued,
                    Stopwatch.GetElapsedTime(judgedAt),
                    DescribeWriteChunk(request, request.StartByte, 1),
                    writeOutcomeUncertain: uncertain));
            throw mapped;
        }
        catch (TransportException ex)
        {
            long faultStart = Stopwatch.GetTimestamp();
            bool reconnectStarted = HandleFault(transport);
            TimeSpan faultElapsed = Stopwatch.GetElapsedTime(faultStart);

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
                        length: 1,
                        cause: CreateFaultDecision(
                            reconnectStarted,
                            faultElapsed,
                            mapped,
                            writeOutcomeUncertain: writeIssued,
                            aftercare: DescribeFaultAftercare(reconnectStarted)));
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

    internal Task<int> WriteAsync(S7WriteRequest request, CancellationToken cancellationToken)
    {
        return WriteOnceAsync(request, cancellationToken);
    }

    private async Task<int> WriteOnceAsync(
        S7WriteRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, request.Data.Length),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        ITcpTransport transport = _transport!;
        await DrainPendingAsync(transport, Volatile.Read(ref _connectionEpoch), cancellationToken)
            .ConfigureAwait(false);

        int payloadLimit = NegotiatedPduSize - ProtocolConstants.WriteRequestOverhead;
        int totalLength = request.Data.Length;
        int chunkCount = (totalLength + payloadLimit - 1) / payloadLimit;
        int offset = 0;
        int chunkIndex = 0;
        var scope = new OperationScope();

        while (offset < totalLength)
        {
            ThrowIfCannotSend(
                S7OperationStage.Write,
                cancellationToken,
                offset,
                offset > 0 ? new WrittenRange(request.StartByte, offset) : null,
                writeOutcomeUncertain: false);

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
                long judgedAt = Stopwatch.GetTimestamp();
                bool uncertain = writeIssued;
                WrittenRange? writtenRange = offset > 0
                    ? new WrittenRange(request.StartByte, offset)
                    : null;

                if (writeIssued)
                {
                    PostAftercare(transport, pduReference, S7OperationStage.Write);
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
                    chunkLength,
                    cause: CreateCancelDecision(
                        timeout,
                        writeIssued,
                        Stopwatch.GetElapsedTime(judgedAt),
                        DescribeWriteChunk(request, chunkStart, chunkLength),
                        bytesWritten: offset,
                        writeOutcomeUncertain: uncertain));
                throw mapped;
            }
            catch (TransportException ex)
            {
                bool uncertain = writeIssued;
                WrittenRange? writtenRange = offset > 0
                    ? new WrittenRange(request.StartByte, offset)
                    : null;

                long faultStart = Stopwatch.GetTimestamp();
                bool reconnectStarted = HandleFault(transport);
                TimeSpan faultElapsed = Stopwatch.GetElapsedTime(faultStart);

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
                    chunkLength,
                    cause: CreateFaultDecision(
                        reconnectStarted,
                        faultElapsed,
                        mapped,
                        offset,
                        uncertain,
                        DescribeFaultAftercare(reconnectStarted)));
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

        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _aftercare.Clear();
        CancelConnectionLost();

        try
        {
            if (State == S7SessionState.Reconnecting)
            {
                await AbortReconnectAsync().ConfigureAwait(false);
            }

            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _sideEffects.CompleteAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            finally
            {
                _shutdownCts.Dispose();
                _gate.Dispose();
            }
        }
    }

    private Task EnsureConnectedAsync(
        S7OperationStage stage,
        CancellationToken cancellationToken)
    {
        S7SessionState state = State;
        if (state == S7SessionState.Connected)
        {
            return Task.CompletedTask;
        }

        long startTimestamp = Stopwatch.GetTimestamp();

        if (state == S7SessionState.Reconnecting)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Cancelled,
                    S7ErrorCode.OperationCancelled,
                    stage,
                    "操作已取消：会话正在自动重连。");
            }

            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionReconnecting,
                stage,
                "会话正在自动重连，请求无法执行。",
                innerException: new ReconnectDecisionInfo(
                    "进入时重连中",
                    Stopwatch.GetElapsedTime(startTimestamp)));
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

    private async Task RunReconnectAsync(ITcpTransport? legacyTransport, CancellationToken cancellationToken)
    {
        _aftercare.Clear();

        if (legacyTransport is not null)
        {
            try
            {
                await legacyTransport.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        DateTimeOffset disconnectedAt = _timeProvider.GetUtcNow();
        int attempt = 0;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                return;
            }

            attempt++;
            TimeSpan delay = BackoffDelayCalculator.Calculate(attempt);

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                    return;
                }
            }

            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                return;
            }

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
                PduNegotiation negotiation = await HandshakeAsync(transport, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                    FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                    return;
                }

                _transport = transport;
                Guid newSessionId = Guid.NewGuid();
                DateTimeOffset connectedAt = _timeProvider.GetUtcNow();
                if (!TryTransition(
                        S7SessionState.Reconnecting,
                        S7SessionState.Connected,
                        newSessionId,
                        negotiation.Effective,
                        out _))
                {
                    _transport = null;
                    await transport.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                LogReconnect(
                    S7LogLevel.Information,
                    succeeded: true,
                    attempt,
                    delay,
                    connectedAt - disconnectedAt,
                    negotiatedPduSize: negotiation.Effective);
                return;
            }
            catch (OperationCanceledException)
            {
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

                FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                return;
            }
            catch (S7ClientException exception)
            {
                if (transport is not null)
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }

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
                    FinishAborted(_timeProvider.GetUtcNow() - disconnectedAt);
                    return;
                }

                LogReconnect(
                    S7LogLevel.Warning,
                    succeeded: false,
                    attempt,
                    delay,
                    disconnectedDuration: null,
                    errorCode: S7ErrorCode.ConnectionReset,
                    exception: exception);
            }
        }
    }

    private void FinishAborted(TimeSpan? disconnectedDuration = null)
    {
        if (!TryTransition(
                S7SessionState.Reconnecting,
                S7SessionState.Disconnected,
                Guid.Empty,
                0,
                out _))
        {
            return;
        }

        LogReconnect(
            S7LogLevel.Warning,
            succeeded: false,
            attempt: null,
            TimeSpan.Zero,
            disconnectedDuration);
    }

    private async Task AbortReconnectAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_sync)
        {
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

        FinishAborted();
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
        }

        cancellation?.Dispose();
    }

    private bool HandleFault(
        ITcpTransport transport,
        S7ErrorCategory category = S7ErrorCategory.Connection,
        bool callerCancelled = false)
    {
        bool shouldReconnect = ReconnectFaultClassifier.ShouldReconnect(
            _options.AutoReconnectEnabled,
            State,
            callerCancelled,
            category);

        S7SessionState target = shouldReconnect
            ? S7SessionState.Reconnecting
            : S7SessionState.Faulted;

        if (!TryBeginFaultTransition(transport, target, out ITcpTransport? ownedLegacy, out CancellationTokenSource? reconnectCts))
        {
            return false;
        }

        if (target == S7SessionState.Reconnecting)
        {
            StartReconnectLoop(ownedLegacy, reconnectCts!);
            return true;
        }

        if (ownedLegacy is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ownedLegacy.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            });
        }

        return false;
    }

    /// <summary>
    /// 以专用线程启动后台重连循环，并发布重连任务句柄。
    /// </summary>
    /// <param name="legacyTransport">待关闭的旧连接。</param>
    /// <param name="reconnectCts">重连取消源。</param>
    /// <returns>重连循环任务。</returns>
    internal Task StartReconnectLoop(ITcpTransport? legacyTransport, CancellationTokenSource reconnectCts)
    {
        CancellationToken token = reconnectCts.Token;
        Task task = Task.Factory
            .StartNew(
                () => RunReconnectAsync(legacyTransport, token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();

        lock (_sync)
        {
            if (ReferenceEquals(_reconnectCancellation, reconnectCts))
            {
                _reconnectTask = task;
            }
        }

        return task;
    }

    internal bool TryBeginFaultTransition(
        ITcpTransport transport,
        S7SessionState target,
        out ITcpTransport? ownedLegacy,
        out CancellationTokenSource? reconnectCts)
    {
        lock (_sync)
        {
            ownedLegacy = null;
            reconnectCts = null;

            if ((S7SessionState)_state != S7SessionState.Connected
                || !ReferenceEquals(_transport, transport))
            {
                return false;
            }

            _state = (int)target;
            _sessionId = Guid.Empty;
            _negotiatedPdu = 0;
            Interlocked.Increment(ref _connectionEpoch);
            ownedLegacy = _transport;
            _transport = null;
            CancelConnectionLostLocked();
            _reconnectCancellation?.Dispose();
            _reconnectCancellation = null;
            _reconnectTask = null;

            if (target == S7SessionState.Reconnecting)
            {
                var cancellation = new CancellationTokenSource();
                _reconnectCancellation = cancellation;
                reconnectCts = cancellation;
            }

            SessionSnapshot snapshot = CreateSnapshotLocked();
            EnqueueStateChangedLocked(S7SessionState.Connected, target, snapshot);
            return true;
        }
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
            Guid.Empty,
            0,
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
        if (!BeginFaultAfterLateResponse(transport, out ITcpTransport? ownedLegacy))
        {
            return;
        }

        if (ownedLegacy is not null)
        {
            try
            {
                await ownedLegacy.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// 迟到响应排空失败后的故障迁移：锁内迁 Faulted、摘连接、通知入队，不启动重连。
    /// </summary>
    /// <param name="transport">发生排空失败的传输实例。</param>
    /// <param name="ownedLegacy">迁移成功时归属调用方的旧连接。</param>
    /// <returns>迁移成功返回 <see langword="true"/>。</returns>
    internal bool BeginFaultAfterLateResponse(ITcpTransport transport, out ITcpTransport? ownedLegacy)
    {
        lock (_sync)
        {
            ownedLegacy = null;

            if ((S7SessionState)_state != S7SessionState.Connected
                || !ReferenceEquals(_transport, transport))
            {
                return false;
            }

            _state = (int)S7SessionState.Faulted;
            _sessionId = Guid.Empty;
            _negotiatedPdu = 0;
            Interlocked.Increment(ref _connectionEpoch);
            ownedLegacy = _transport;
            _transport = null;
            CancelConnectionLostLocked();
            _reconnectCancellation?.Dispose();
            _reconnectCancellation = null;
            _reconnectTask = null;

            SessionSnapshot snapshot = CreateSnapshotLocked();
            EnqueueStateChangedLocked(S7SessionState.Connected, S7SessionState.Faulted, snapshot);
            return true;
        }
    }

    private async ValueTask DrainPendingAsync(
        ITcpTransport currentTransport,
        long currentEpoch,
        CancellationToken callerToken)
    {
        while (_aftercare.TryTake(out PendingLateResponse item))
        {
            if (item.ConnectionEpoch != currentEpoch
                || !ReferenceEquals(item.Transport, currentTransport))
            {
                continue;
            }

            if (callerToken.IsCancellationRequested)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Cancelled,
                    S7ErrorCode.OperationCancelled,
                    item.Stage,
                    "操作已取消。");
            }

            bool drained = await LateResponseDrainer
                .TryDrainAsync(item.Transport, item.PduReference, item.DrainTimeout, item.Stage)
                .ConfigureAwait(false);
            if (drained)
            {
                continue;
            }

            BeginFaultAfterLateResponse(item.Transport, out ITcpTransport? ownedLegacy);
            if (ownedLegacy is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ownedLegacy.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                });
            }

            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.SessionFaulted,
                item.Stage,
                "会话处于异常状态，必须显式断开后重连。");
        }
    }

    private void PostAftercare(
        ITcpTransport transport,
        ushort pduReference,
        S7OperationStage stage)
    {
        _aftercare.Post(new PendingLateResponse(
            transport,
            pduReference,
            stage,
            GetDrainTimeout(stage),
            Volatile.Read(ref _connectionEpoch)));
    }

    private TimeSpan GetDrainTimeout(S7OperationStage stage)
    {
        TimeSpan configured = stage == S7OperationStage.Read ? _options.ReadTimeout : _options.WriteTimeout;
        return configured < TimeSpan.FromSeconds(2) ? configured : TimeSpan.FromSeconds(2);
    }

    private bool TryTransition(
        S7SessionState expected,
        S7SessionState target,
        Guid sessionId,
        int pdu,
        out SessionSnapshot snapshot)
    {
        lock (_sync)
        {
            S7SessionState previous = (S7SessionState)_state;
            if (previous != expected)
            {
                snapshot = CreateSnapshotLocked();
                return false;
            }

            _state = (int)target;
            _sessionId = sessionId;
            _negotiatedPdu = pdu;

            if (target is S7SessionState.Disconnected or S7SessionState.Faulted
                || (target == S7SessionState.Connected && previous != S7SessionState.Connected))
            {
                Interlocked.Increment(ref _connectionEpoch);
            }

            if (target == S7SessionState.Connected)
            {
                CancelConnectionLostLocked();
                _connectionLostCts = new CancellationTokenSource();
                _reconnectCancellation?.Dispose();
                _reconnectCancellation = null;
                _reconnectTask = null;
            }
            else if (previous == S7SessionState.Connected)
            {
                CancelConnectionLostLocked();
            }

            snapshot = CreateSnapshotLocked();
            EnqueueStateChangedLocked(previous, target, snapshot);
            return true;
        }
    }

    private SessionSnapshot CreateSnapshotLocked()
    {
        return new SessionSnapshot
        {
            State = (S7SessionState)_state,
            SessionId = _sessionId,
            NegotiatedPduSize = _negotiatedPdu,
            AutoReconnectEnabled = _options.AutoReconnectEnabled
        };
    }

    private void EnqueueStateChangedLocked(
        S7SessionState previous,
        S7SessionState current,
        SessionSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _sideEffects.PostNotification(previous, current, snapshot.SessionId);
    }

    private void DispatchStateChanged(S7SessionStateChangedEventArgs args)
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

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<S7SessionStateChangedEventArgs>)handler)(this, args);
            }
            catch (Exception)
            {
            }
        }
    }

    private CancellationToken GetConnectionLostToken()
    {
        try
        {
            CancellationTokenSource? cancellation = Volatile.Read(ref _connectionLostCts);
            return cancellation?.Token ?? CancellationToken.None;
        }
        catch (ObjectDisposedException)
        {
            return CancellationToken.None;
        }
    }

    private void CancelConnectionLost()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _connectionLostCts, null);
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation.Dispose();
    }

    private void CancelConnectionLostLocked()
    {
        CancellationTokenSource? cancellation = _connectionLostCts;
        _connectionLostCts = null;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellation.Dispose();
    }

    private async ValueTask<RequestGate.Lease> AcquireGateAsync(
        S7OperationStage stage,
        string context,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new S7ClientException(
                S7ErrorCategory.Cancelled,
                S7ErrorCode.OperationCancelled,
                stage,
                $"操作已取消：{context}。");
        }

        if (_gate.TryAcquire(stage, out RequestGate.Lease? fastLease))
        {
            return fastLease!;
        }

        if (State != S7SessionState.Connected)
        {
            await EnsureConnectedAsync(stage, cancellationToken).ConfigureAwait(false);
        }

        CancellationToken lostToken = GetConnectionLostToken();
        using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(lostToken, _shutdownToken);
        try
        {
            return await _gate.AcquireAsync(stage, cancellationToken, interrupt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Cancelled,
                    S7ErrorCode.OperationCancelled,
                    stage,
                    $"操作已取消：{context}。",
                    innerException: ex);
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(S7Session));
            }

            if (State == S7SessionState.Reconnecting)
            {
                throw CreateReconnectingException(stage, "排队中止");
            }

            await EnsureConnectedAsync(stage, cancellationToken).ConfigureAwait(false);

            try
            {
                return await _gate.AcquireAsync(stage, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException retryException)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Cancelled,
                    S7ErrorCode.OperationCancelled,
                    stage,
                    $"操作已取消：{context}。",
                    innerException: retryException);
            }
        }
    }

    private void ThrowIfCannotSend(
        S7OperationStage stage,
        CancellationToken callerToken,
        int bytesWritten = 0,
        WrittenRange? writtenRange = null,
        bool writeOutcomeUncertain = false)
    {
        if (callerToken.IsCancellationRequested)
        {
            throw new S7ClientException(
                S7ErrorCategory.Cancelled,
                S7ErrorCode.OperationCancelled,
                stage,
                "操作已取消。");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(S7Session));
        }

        S7SessionState state = State;
        if (state == S7SessionState.Connected)
        {
            return;
        }

        long startTimestamp = Stopwatch.GetTimestamp();

        if (state == S7SessionState.Reconnecting)
        {
            if (callerToken.IsCancellationRequested)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Cancelled,
                    S7ErrorCode.OperationCancelled,
                    stage,
                    "操作已取消：会话正在自动重连。");
            }

            throw CreateReconnectingException(
                stage,
                "分包中止",
                bytesWritten,
                writtenRange,
                writeOutcomeUncertain,
                Stopwatch.GetElapsedTime(startTimestamp));
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

    private S7ClientException CreateReconnectingException(
        S7OperationStage stage,
        string reason,
        int bytesWritten = 0,
        WrittenRange? writtenRange = null,
        bool writeOutcomeUncertain = false,
        TimeSpan? elapsed = null)
    {
        var decision = new ReconnectDecisionInfo(
            reason,
            elapsed ?? TimeSpan.Zero,
            bytesWritten,
            writeOutcomeUncertain);

        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.SessionReconnecting,
            stage,
            "会话正在自动重连，请求无法执行。",
            bytesWritten: bytesWritten,
            writtenRange: writtenRange,
            writeOutcomeUncertain: writeOutcomeUncertain,
            innerException: decision);
    }

    private ReconnectDecisionInfo CreateFaultDecision(
        bool reconnectStarted,
        TimeSpan elapsed,
        S7ClientException mapped,
        int bytesWritten = 0,
        bool writeOutcomeUncertain = false,
        string? aftercare = null)
    {
        string reason;
        if (reconnectStarted)
        {
            reason = "已启动后台重连";
        }
        else if (!_options.AutoReconnectEnabled)
        {
            reason = "未启动后台重连（开关关闭）";
        }
        else if (State == S7SessionState.Reconnecting)
        {
            reason = "已由其它调用启动后台重连";
        }
        else if (State == S7SessionState.Faulted)
        {
            reason = "未启动后台重连（会话异常）";
        }
        else
        {
            reason = "未启动后台重连";
        }

        if (aftercare is not null)
        {
            reason = $"{reason}；{aftercare}";
        }

        return new ReconnectDecisionInfo(reason, elapsed, bytesWritten, writeOutcomeUncertain, mapped);
    }

    private static ReconnectDecisionInfo CreateCancelDecision(
        OperationTimeout timeout,
        bool requestIssued,
        TimeSpan elapsed,
        string context,
        int bytesWritten = 0,
        bool writeOutcomeUncertain = false)
    {
        string trigger = timeout.CallerTriggered ? "调用方取消" : "超时";
        string aftercare = requestIssued
            ? "未重试、未退避；善后已延后（迟到响应由下一次持许可调用清理）"
            : "未重试、未退避；无善后（未发出帧）";

        return new ReconnectDecisionInfo(
            $"{trigger}：{context}；{aftercare}",
            elapsed,
            bytesWritten,
            writeOutcomeUncertain);
    }

    private string DescribeFaultAftercare(bool reconnectStarted)
    {
        if (reconnectStarted)
        {
            return "未重试、未退避；善后已延后（旧连接由后台重连关闭）";
        }

        if (!_options.AutoReconnectEnabled)
        {
            return "未重试、未退避；无善后（自动重连已关闭）";
        }

        return "未重试、未退避；无善后（未启动后台重连）";
    }

    internal void PostFailureLog(S7LogLevel level, in S7LogEntry entry, Exception? cause)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _sideEffects.PostFailureLog(level, in entry, cause);
    }

    internal void PostFailureLog(in FailureLogRequest request)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _sideEffects.PostFailureLog(in request);
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
        int? length = null,
        Exception? cause = null,
        TimeSpan? judgmentElapsed = null)
    {
        Exception? logCause = cause;
        if (logCause is null && exception.InnerException is ReconnectDecisionInfo decision)
        {
            logCause = decision;
        }

        logCause ??= exception;

        var request = new FailureLogRequest
        {
            Level = S7LogLevel.Warning,
            Operation = operation,
            Stage = stage,
            Area = area,
            DbNumber = dbNumber,
            StartByte = startByte,
            Length = length,
            Elapsed = scope.Elapsed,
            ErrorCode = exception.ErrorCode,
            PlcReturnCode = exception.PlcReturnCode,
            JudgmentElapsed = judgmentElapsed ?? TimeSpan.Zero,
            Cause = logCause
        };

        PostFailureLog(in request);
    }

    private void LogReconnect(
        S7LogLevel level,
        bool succeeded,
        int? attempt,
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
            ReconnectDelay = reconnectDelay,
            DisconnectedDuration = disconnectedDuration
        };

        _logger.Log(effectiveLevel, in entry, exception);
    }
}