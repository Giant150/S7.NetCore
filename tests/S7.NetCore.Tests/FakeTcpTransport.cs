using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests;

internal sealed class FakeTcpTransport : ITcpTransport
{
    private readonly object _sync = new();
    private readonly Queue<byte[]> _responses = new();
    private readonly SemaphoreSlim _signal = new(0);
    private byte[]? _current;
    private int _currentOffset;
    private volatile bool _open;
    private int _requestsSinceConnect;
    private int _failureConsumed;
    private int _readCount;

    public List<byte[]> ReceivedFrames { get; } = [];

    internal int WriteCount => ReceivedFrames.Count;

    public int ReadCount => Volatile.Read(ref _readCount);

    public int ConnectedCount { get; private set; }

    public int ConfirmedPduSize { get; set; } = 480;

    public int FragmentSize { get; set; }

    public TimeSpan ReadResponseDelay { get; set; }

    public TimeSpan WriteResponseDelay { get; set; }

    public TimeSpan DisposeDelay { get; set; }

    public Func<int, byte>? ReadReturnCodeProvider { get; set; }

    public Func<int, byte>? WriteReturnCodeProvider { get; set; }

    public int TruncateReadBytes { get; set; }

    public Func<byte[], byte[]?>? RequestInterceptor { get; set; }

    public byte[] Memory { get; } = new byte[65536];

    public int FailAfterRequestCount { get; set; } = -1;

    public bool FailOnce { get; set; } = true;

    public int FailConnectAttempts { get; set; }

    public bool IsOpen => _open;

    public Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (FailConnectAttempts > 0)
        {
            FailConnectAttempts--;
            throw new TransportException(S7ErrorCode.TcpConnectFailed, "模拟连接失败。");
        }

        _open = true;
        ConnectedCount++;
        Interlocked.Exchange(ref _requestsSinceConnect, 0);
        return Task.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!_open)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "连接未建立。");
        }

        byte[] request = buffer.ToArray();
        ReceivedFrames.Add(request);

        int requestIndex = Interlocked.Increment(ref _requestsSinceConnect);
        bool failNow = FailAfterRequestCount > 0
            && requestIndex >= FailAfterRequestCount
            && (!FailOnce || Interlocked.Exchange(ref _failureConsumed, 1) == 0);
        if (failNow)
        {
            _open = false;
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }

            return ValueTask.CompletedTask;
        }

        byte[]? response = RequestInterceptor is null
            ? DefaultResponse(request)
            : RequestInterceptor(request);

        if (response is not null)
        {
            bool isRead = request.Length > 17 && request[17] == 0x04;
            EnqueueResponse(response, isRead ? ReadResponseDelay : WriteResponseDelay);
        }

        return ValueTask.CompletedTask;
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
                    if (FragmentSize > 0)
                    {
                        count = Math.Min(count, FragmentSize);
                    }

                    _current.AsSpan(_currentOffset, count).CopyTo(buffer.Span);
                    _currentOffset += count;
                    Interlocked.Increment(ref _readCount);
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

    public async ValueTask DisposeAsync()
    {
        if (DisposeDelay > TimeSpan.Zero)
        {
            await Task.Delay(DisposeDelay).ConfigureAwait(false);
        }

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

        return;
    }

    private void EnqueueResponse(byte[] response, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
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
            await Task.Delay(delay).ConfigureAwait(false);
            lock (_sync)
            {
                _responses.Enqueue(response);
            }

            _signal.Release();
        });
    }

    internal byte[]? DefaultResponseForTest(byte[] request)
    {
        return DefaultResponse(request);
    }

    private byte[]? DefaultResponse(byte[] request)
    {
        if (request.Length < 7)
        {
            return null;
        }

        if (request[5] == 0xE0)
        {
            return BuildConnectionConfirm();
        }

        if (request.Length < 18 || request[7] != 0x32)
        {
            return null;
        }

        return request[17] switch
        {
            0xF0 => BuildSetupResponse(request),
            0x04 => BuildReadResponse(request),
            0x05 => BuildWriteResponse(request),
            _ => null
        };
    }

    private static byte[] BuildConnectionConfirm()
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
        frame[7] = 0x32;
        frame[8] = 0x03;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), reference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x0008);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(17), 0x0000);
        frame[19] = 0xF0;
        frame[20] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(21), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(25), (ushort)ConfirmedPduSize);

        return frame;
    }

    private byte[] BuildReadResponse(byte[] request)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(23));
        int startByte = ((request[28] << 16) | (request[29] << 8) | request[30]) / 8;

        byte returnCode = ReadReturnCodeProvider?.Invoke(startByte)
            ?? (startByte + count <= Memory.Length ? (byte)0xFF : (byte)0x05);

        int actualCount = returnCode == 0xFF
            ? Math.Max(0, count - TruncateReadBytes)
            : 0;

        ushort reference = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(11));
        int payloadLength = 4 + actualCount;
        var frame = new byte[7 + 12 + 2 + payloadLength];

        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.Read);
        frame[4] = 0x02;
        frame[5] = 0xF0;
        frame[6] = 0x80;
        frame[7] = 0x32;
        frame[8] = 0x03;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), reference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x0002);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), (ushort)payloadLength);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(17), 0x0000);
        frame[19] = 0x04;
        frame[20] = 0x01;
        frame[21] = returnCode;
        frame[22] = 0x04;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), (ushort)(actualCount * 8));

        if (actualCount > 0)
        {
            int available = Math.Min(actualCount, Memory.Length - startByte);
            Memory.AsSpan(startByte, available).CopyTo(frame.AsSpan(25));
        }

        return frame;
    }

    private byte[] BuildWriteResponse(byte[] request)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(23));
        int startByte = ((request[28] << 16) | (request[29] << 8) | request[30]) / 8;

        byte returnCode = WriteReturnCodeProvider?.Invoke(startByte)
            ?? (startByte + count <= Memory.Length ? (byte)0xFF : (byte)0x05);

        if (returnCode == 0xFF)
        {
            if (request.Length >= ProtocolConstants.WriteBitRequestFrameSize
                && request[22] == ProtocolConstants.BitTransportSizeParameter)
            {
                int bitAddress = (request[28] << 16) | (request[29] << 8) | request[30];
                int bitStartByte = bitAddress / 8;
                int bitNumber = bitAddress % 8;

                if (bitStartByte < Memory.Length)
                {
                    if ((request[35] & 0x01) != 0)
                    {
                        Memory[bitStartByte] |= (byte)(1 << bitNumber);
                    }
                    else
                    {
                        Memory[bitStartByte] &= (byte)~(1 << bitNumber);
                    }
                }
            }
            else
            {
                request.AsSpan(ProtocolConstants.WriteRequestOverhead, count).CopyTo(Memory.AsSpan(startByte));
            }
        }

        ushort reference = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(11));
        var frame = new byte[22];

        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.Write);
        frame[4] = 0x02;
        frame[5] = 0xF0;
        frame[6] = 0x80;
        frame[7] = 0x32;
        frame[8] = 0x03;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), reference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x0002);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(17), 0x0000);
        frame[19] = 0x05;
        frame[20] = 0x01;
        frame[21] = returnCode;

        return frame;
    }
}