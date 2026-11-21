using System.Buffers;

namespace S7.NetCore.Internal;

/// <summary>
/// S7 会话：状态机、超时编排、分包读写与结果装配。
/// </summary>
internal sealed class S7Session : IAsyncDisposable
{
    private readonly S7ConnectionOptions _options;
    private readonly IS7Logger _logger;
    private readonly ITcpTransport? _injectedTransport;
    private readonly RequestGate _gate;
    private readonly PduReferenceGenerator _references = new();
    private readonly TimeProvider _timeProvider;

    private int _state = (int)S7SessionState.Disconnected;
    private int _disposed;
    private int _negotiatedPdu;
    private Guid _sessionId = Guid.Empty;
    private volatile ITcpTransport? _transport;

    internal S7Session(
        S7ConnectionOptions options,
        IS7Logger? logger,
        ITcpTransport? transport,
        TimeProvider? timeProvider)
    {
        _options = options;
        _logger = logger is null or NullS7Logger ? NullS7Logger.Instance : new SafeS7Logger(logger);
        _injectedTransport = transport;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _gate = new RequestGate(options.MaxQueuedRequests);
    }

    internal S7SessionState State => (S7SessionState)Volatile.Read(ref _state);

    internal int NegotiatedPduSize => Volatile.Read(ref _negotiatedPdu);

    internal SessionSnapshot Snapshot => new()
    {
        State = State,
        SessionId = _sessionId,
        NegotiatedPduSize = NegotiatedPduSize
    };

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        int previous = Interlocked.CompareExchange(
            ref _state,
            (int)S7SessionState.Connecting,
            (int)S7SessionState.Disconnected);

        if (previous != (int)S7SessionState.Disconnected)
        {
            bool faulted = previous == (int)S7SessionState.Faulted;
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                faulted ? S7ErrorCode.SessionFaulted : S7ErrorCode.SessionAlreadyConnected,
                S7OperationStage.Connect,
                faulted
                    ? "会话处于异常状态，必须显式断开后重连。"
                    : "会话已存在或正在连接中，禁止建立第二条并行会话。");
        }

        var scope = new OperationScope();
        ITcpTransport transport = _injectedTransport ?? new TcpTransport();
        _transport = transport;
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
            PduNegotiation negotiation = PduSizeNegotiator.Negotiate(_options.RequestedPduSize, confirmedPduSize);

            Volatile.Write(ref _negotiatedPdu, negotiation.Effective);
            _sessionId = Guid.NewGuid();
            Volatile.Write(ref _state, (int)S7SessionState.Connected);

            Log(scope, "Connect", S7OperationStage.S7Setup, succeeded: true, negotiatedPduSize: negotiation.Effective);
        }
        catch (OperationCanceledException ex)
        {
            await FailConnectAsync(transport).ConfigureAwait(false);
            S7ClientException mapped = MapCanceled(
                timeout,
                stage,
                S7ErrorCode.ConnectTimeout,
                $"连接 {_options.IpAddress}:{_options.Port}（阶段 {stage}）",
                ex);
            LogFailure(scope, "Connect", stage, mapped);
            throw mapped;
        }
        catch (TransportException ex)
        {
            await FailConnectAsync(transport).ConfigureAwait(false);
            S7ClientException mapped = MapTransport(ex, stage);
            LogFailure(scope, "Connect", stage, mapped);
            throw mapped;
        }
        catch (S7ClientException ex)
        {
            await FailConnectAsync(transport).ConfigureAwait(false);
            LogFailure(scope, "Connect", stage, ex);
            throw;
        }
        catch (Exception ex)
        {
            await FailConnectAsync(transport).ConfigureAwait(false);
            var mapped = new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.InvalidFrame,
                stage,
                $"连接过程发生未预期错误（阶段 {stage}）：{ex.Message}",
                innerException: ex);
            LogFailure(scope, "Connect", stage, mapped);
            throw mapped;
        }
    }

    internal async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        int current = Volatile.Read(ref _state);
        if (current == (int)S7SessionState.Disconnected)
        {
            return;
        }

        RequestGate.Lease? lease = null;
        var scope = new OperationScope();
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
            ITcpTransport? transport = _transport;
            _transport = null;
            Volatile.Write(ref _negotiatedPdu, 0);
            Volatile.Write(ref _state, (int)S7SessionState.Disconnected);
            _sessionId = Guid.Empty;

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
        EnsureConnected(S7OperationStage.Read);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Read,
                DescribeReadChunk(request, request.StartByte, request.Length),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureConnected(S7OperationStage.Read);

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
                    if (!drained)
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
                    await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
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
        EnsureConnected(S7OperationStage.Write);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, 1),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureConnected(S7OperationStage.Write);

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

        try
        {
            TraceFrame("Write", S7OperationStage.Write, frame);
            await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
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
            if (!timeout.CallerTriggered)
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
                writeOutcomeUncertain: true);
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
            await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
            var mapped = new S7ClientException(
                S7ErrorCategory.Connection,
                S7ErrorCode.ConnectionReset,
                S7OperationStage.Write,
                $"位写入过程中连接中断：{DescribeWriteChunk(request, request.StartByte, 1)}，位号 {bitNumber}。",
                bytesWritten: 0,
                writtenRange: null,
                writeOutcomeUncertain: true,
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
        EnsureConnected(S7OperationStage.Write);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeWriteChunk(request, request.StartByte, request.Data.Length),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureConnected(S7OperationStage.Write);

        int payloadLimit = NegotiatedPduSize - ProtocolConstants.WriteRequestOverhead;
        int totalLength = request.Data.Length;
        int chunkCount = (totalLength + payloadLimit - 1) / payloadLimit;
        int bytesWritten = 0;
        int chunkIndex = 0;
        var scope = new OperationScope();
        ITcpTransport transport = _transport!;

        while (bytesWritten < totalLength)
        {
            chunkIndex++;
            int chunkLength = Math.Min(payloadLimit, totalLength - bytesWritten);
            int chunkStart = request.StartByte + bytesWritten;
            ReadOnlySpan<byte> chunk = request.Data.Span.Slice(bytesWritten, chunkLength);

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
                byte[] payload = await S7FrameReader
                    .ReadS7PayloadAsync(transport, S7OperationStage.Write, timeout.Token)
                    .ConfigureAwait(false);

                WriteResponseInfo info = S7ResponseParser.ParseWriteResponse(payload, pduReference);
                if (info.ReturnCode != ProtocolConstants.SuccessReturnCode)
                {
                    WrittenRange? failedRange = bytesWritten > 0
                        ? new WrittenRange(request.StartByte, bytesWritten)
                        : null;

                    throw new S7ClientException(
                        S7ErrorCategory.PlcReturn,
                        PlcReturnCodeMapper.Map(info.ReturnCode).Code,
                        S7OperationStage.Write,
                        $"写入失败：{PlcReturnCodeMapper.Map(info.ReturnCode).Message}"
                        + $"（返回码 0x{info.ReturnCode:X2}），{DescribeWriteChunk(request, chunkStart, chunkLength)}。",
                        plcReturnCode: info.ReturnCode,
                        bytesWritten: bytesWritten,
                        writtenRange: failedRange);
                }

                bytesWritten += chunkLength;
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
                WrittenRange? writtenRange = bytesWritten > 0
                    ? new WrittenRange(request.StartByte, bytesWritten)
                    : null;

                if (!timeout.CallerTriggered)
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
                    bytesWritten,
                    writtenRange,
                    writeOutcomeUncertain: true);
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
                WrittenRange? writtenRange = bytesWritten > 0
                    ? new WrittenRange(request.StartByte, bytesWritten)
                    : null;

                await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                var mapped = new S7ClientException(
                    S7ErrorCategory.Connection,
                    S7ErrorCode.ConnectionReset,
                    S7OperationStage.Write,
                    $"写入过程中连接中断：{DescribeWriteChunk(request, chunkStart, chunkLength)}。",
                    bytesWritten: bytesWritten,
                    writtenRange: writtenRange,
                    writeOutcomeUncertain: true,
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
        return bytesWritten;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
    }

    private void EnsureConnected(S7OperationStage stage)
    {
        S7SessionState state = State;
        if (state == S7SessionState.Connected)
        {
            return;
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

    private async ValueTask FailConnectAsync(ITcpTransport transport)
    {
        _transport = null;
        Volatile.Write(ref _negotiatedPdu, 0);
        Volatile.Write(ref _state, (int)S7SessionState.Disconnected);
        _sessionId = Guid.Empty;
        await transport.DisposeAsync().ConfigureAwait(false);
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
        Volatile.Write(ref _state, (int)S7SessionState.Faulted);
        Volatile.Write(ref _negotiatedPdu, 0);
        await transport.DisposeAsync().ConfigureAwait(false);
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
        Exception? exception = null)
    {
        S7LogLevel level = protocolTrace is null
            ? (succeeded ? S7LogLevel.Information : S7LogLevel.Warning)
            : S7LogLevel.Trace;

        if (!_logger.IsEnabled(level))
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
            ProtocolTrace = protocolTrace
        };

        _logger.Log(level, in entry, exception);
    }
}