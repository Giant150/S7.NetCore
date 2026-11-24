using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>
/// 支持多条目读/写应答的内存 PLC 模拟传输层。用于批量门面的确定性端到端验证。
/// </summary>
internal sealed class BatchPlcSimulator : ITcpTransport
{
    private readonly object _sync = new();
    private readonly Queue<byte[]> _responses = new();
    private readonly SemaphoreSlim _signal = new(0);
    private byte[]? _current;
    private int _currentOffset;
    private volatile bool _open;
    private int _readFailureConsumed;
    private int _writeSuppressConsumed;

    internal byte[] Memory { get; } = new byte[65536];

    internal int ConfirmedPduSize { get; set; } = 480;

    internal List<byte[]> Frames { get; } = [];

    internal int ReadFrameCount { get; private set; }

    internal int WriteFrameCount { get; private set; }

    internal Func<int, (byte ReturnCode, int DeclaredBits)>? ReadItemProvider { get; set; }

    internal Func<int, byte>? WriteReturnCodeProvider { get; set; }

    /// <summary>为真时批量读请求不返回任何响应，用于验证读超时。</summary>
    internal bool SuppressReadResponses { get; set; }

    /// <summary>为真时批量写请求不返回任何响应，用于验证写超时与结果不确定标记。</summary>
    internal bool SuppressWriteResponses { get; set; }

    /// <summary>批量写请求到达时抛出传输异常，用于验证连接中断归因。</summary>
    internal bool ThrowOnWrite { get; set; }

    /// <summary>读响应参数区的条目数覆盖值；-1 表示使用请求条目数。</summary>
    internal int ResponseItemCountOverride { get; set; } = -1;

    /// <summary>为真时首次批量写不返回响应（超时），用于验证结果不确定后的整批重放。</summary>
    internal bool SuppressFirstWriteOnce { get; set; }

    /// <summary>为真时首次批量读以传输异常中断连接，用于验证重连后整批重试。</summary>
    internal bool FailFirstReadOnce { get; set; }

    /// <summary>响应入队前的延迟，用于验证调用方取消。</summary>
    internal TimeSpan ResponseDelay { get; set; }

    public bool IsOpen => _open;

    public Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _open = true;
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!_open)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "模拟传输层未建立连接。");
        }

        byte[] request = buffer.ToArray();
        lock (_sync)
        {
            Frames.Add(request);
        }

        if (request.Length > 18 && request[7] == ProtocolConstants.S7ProtocolId)
        {
            if (request[17] == ProtocolConstants.S7FunctionRead)
            {
                ReadFrameCount++;
            }
            else if (request[17] == ProtocolConstants.S7FunctionWrite)
            {
                WriteFrameCount++;
            }
        }

        if (ThrowOnWrite && request.Length > 17 && request[17] == ProtocolConstants.S7FunctionWrite)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "模拟批量写传输中断。");
        }

        if (FailFirstReadOnce
            && request.Length > 18
            && request[17] == ProtocolConstants.S7FunctionRead
            && Interlocked.Exchange(ref _readFailureConsumed, 1) == 0)
        {
            _open = false;
            throw new TransportException(S7ErrorCode.ConnectionReset, "模拟批量读连接中断。");
        }

        byte[]? response = Handle(request);
        if (response is not null)
        {
            EnqueueResponse(response);
        }

        return ValueTask.CompletedTask;
    }

    private void EnqueueResponse(byte[] response)
    {
        if (ResponseDelay <= TimeSpan.Zero)
        {
            lock (_sync)
            {
                _responses.Enqueue(response);
            }

            _signal.Release();
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(ResponseDelay).ConfigureAwait(false);
            lock (_sync)
            {
                _responses.Enqueue(response);
            }

            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SemaphoreFullException)
            {
            }
        });
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_sync)
            {
                if (_current is not null && _currentOffset < _current.Length)
                {
                    int count = Math.Min(buffer.Length, _current.Length - _currentOffset);
                    _current.AsSpan(_currentOffset, count).CopyTo(buffer.Span);
                    _currentOffset += count;
                    return count;
                }

                if (_responses.Count > 0)
                {
                    _current = _responses.Dequeue();
                    _currentOffset = 0;
                    continue;
                }
            }

            if (!_open)
            {
                return 0;
            }

            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _open = false;
        lock (_sync)
        {
            _current = null;
            _responses.Clear();
        }

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private byte[]? Handle(byte[] request)
    {
        if (request.Length < 7)
        {
            return null;
        }

        if (request[5] == 0xE0)
        {
            return BuildConnectionConfirm();
        }

        if (request.Length < 18 || request[7] != ProtocolConstants.S7ProtocolId)
        {
            return null;
        }

        return request[17] switch
        {
            ProtocolConstants.S7FunctionRead => SuppressReadResponses ? null : BuildReadResponse(request),
            ProtocolConstants.S7FunctionWrite => SuppressWriteOnceOrAlways() ? null : BuildWriteResponse(request),
            ProtocolConstants.S7FunctionSetup => BuildSetupResponse(request),
            _ => null
        };
    }

    private bool SuppressWriteOnceOrAlways()
    {
        if (SuppressWriteResponses)
        {
            return true;
        }

        return SuppressFirstWriteOnce && Interlocked.Exchange(ref _writeSuppressConsumed, 1) == 0;
    }

    private byte[] BuildConnectionConfirm()
    {
        return
        [
            0x03, 0x00, 0x00, 0x16,
            0x11, 0xD0, 0x00, 0x01, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, 0x01, 0x00,
            0xC2, 0x02, 0x03, 0x01
        ];
    }

    private byte[] BuildSetupResponse(byte[] request)
    {
        ushort reference = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(11));
        var frame = new byte[27];

        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.S7Setup);
        frame[4] = 0x02;
        frame[5] = 0xF0;
        frame[6] = 0x80;
        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = 0x03;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), reference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x0008);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(17), 0x0000);
        frame[19] = ProtocolConstants.S7FunctionSetup;
        frame[20] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(21), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(25), (ushort)ConfirmedPduSize);
        return frame;
    }

    private byte[] BuildReadResponse(byte[] request)
    {
        int count = request[18];
        var data = new List<byte>(count * 8);

        for (int i = 0; i < count; i++)
        {
            int parameterOffset = 19 + (12 * i);
            int length = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(parameterOffset + 4));
            int bitAddress = (request[parameterOffset + 9] << 16)
                | (request[parameterOffset + 10] << 8)
                | request[parameterOffset + 11];
            int startByte = bitAddress / 8;

            (byte returnCode, int declaredBits) = ReadItemProvider?.Invoke(i)
                ?? ((startByte + length <= Memory.Length ? ProtocolConstants.SuccessReturnCode : (byte)0x05, length * 8));
            int actualLength = returnCode == ProtocolConstants.SuccessReturnCode ? declaredBits / 8 : 0;

            data.Add(returnCode);
            data.Add(ProtocolConstants.TransportSizeOctet);
            data.Add((byte)(declaredBits >> 8));
            data.Add((byte)declaredBits);

            for (int offset = 0; offset < actualLength; offset++)
            {
                int address = startByte + offset;
                data.Add(address < Memory.Length ? Memory[address] : (byte)0);
            }

            if ((actualLength & 1) != 0)
            {
                data.Add(0x00);
            }
        }

        int responseItemCount = ResponseItemCountOverride >= 0 ? ResponseItemCountOverride : count;
        return WrapResponse(request, ProtocolConstants.S7FunctionRead, (byte)responseItemCount, data);
    }

    private byte[] BuildWriteResponse(byte[] request)
    {
        int count = request[18];
        int parameterLength = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(13));
        int dataOffset = 17 + parameterLength;
        var codes = new byte[count];

        for (int i = 0; i < count; i++)
        {
            int parameterOffset = 19 + (12 * i);
            bool isBit = request[parameterOffset + 3] == ProtocolConstants.BitTransportSizeParameter;
            int bitAddress = (request[parameterOffset + 9] << 16)
                | (request[parameterOffset + 10] << 8)
                | request[parameterOffset + 11];
            int startByte = bitAddress / 8;
            int bitNumber = bitAddress % 8;
            int actualLength = isBit
                ? 1
                : BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(dataOffset + 2)) / 8;

            byte returnCode = WriteReturnCodeProvider?.Invoke(i)
                ?? (startByte + actualLength <= Memory.Length ? ProtocolConstants.SuccessReturnCode : (byte)0x05);
            codes[i] = returnCode;

            if (returnCode == ProtocolConstants.SuccessReturnCode)
            {
                if (isBit)
                {
                    if (request[dataOffset + 4] != 0)
                    {
                        Memory[startByte] |= (byte)(1 << bitNumber);
                    }
                    else
                    {
                        Memory[startByte] &= (byte)~(1 << bitNumber);
                    }
                }
                else
                {
                    request.AsSpan(dataOffset + 4, actualLength).CopyTo(Memory.AsSpan(startByte));
                }
            }

            dataOffset += ProtocolConstants.WriteItemDataHeaderSize + actualLength;
            if (!isBit && (actualLength & 1) != 0 && i != count - 1)
            {
                dataOffset++;
            }
        }

        return WrapResponse(request, ProtocolConstants.S7FunctionWrite, (byte)count, [.. codes]);
    }

    private static byte[] WrapResponse(byte[] request, byte function, byte itemCount, List<byte> data)
    {
        ushort reference = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(11));
        int payloadLength = ProtocolConstants.S7AckHeaderSize + 2 + data.Count;
        var frame = new byte[ProtocolConstants.TpktHeaderSize + ProtocolConstants.CotpDataHeaderSize + payloadLength];

        TpktCodec.WriteHeader(frame, frame.Length, function == ProtocolConstants.S7FunctionRead
            ? S7OperationStage.Read
            : S7OperationStage.Write);
        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;
        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrAckData;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), reference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 2);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), (ushort)data.Count);
        frame[19] = function;
        frame[20] = itemCount;
        data.CopyTo(frame, 21);
        return frame;
    }
}