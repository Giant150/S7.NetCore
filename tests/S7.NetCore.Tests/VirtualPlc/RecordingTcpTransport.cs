using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 真实请求帧记录。
/// </summary>
/// <param name="Index">发送顺序（0 起）。</param>
/// <param name="Data">请求帧字节。</param>
internal readonly record struct RecordedFrame(int Index, byte[] Data);

/// <summary>
/// 真实 TCP 传输的录制装饰器：只记录请求帧，透传读写，不产生任何响应。
/// </summary>
internal sealed class RecordingTcpTransport : ITcpTransport
{
    private readonly ITcpTransport _inner;
    private readonly object _sync = new();
    private readonly List<RecordedFrame> _frames = [];
    private int _writeCount;
    private int _disposed;

    internal RecordingTcpTransport(ITcpTransport inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>按发送顺序记录的请求帧快照。</summary>
    internal IReadOnlyList<RecordedFrame> Frames
    {
        get
        {
            lock (_sync)
            {
                return _frames.ToArray();
            }
        }
    }

    /// <summary>已发起的写调用次数。</summary>
    internal int WriteCount => Volatile.Read(ref _writeCount);

    /// <inheritdoc />
    public bool IsOpen => _inner.IsOpen;

    /// <inheritdoc />
    public Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _inner.ConnectAsync(host, port, timeout, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        return _inner.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        int index = Interlocked.Increment(ref _writeCount) - 1;
        byte[] copy = buffer.ToArray();

        lock (_sync)
        {
            _frames.Add(new RecordedFrame(index, copy));
        }

        return _inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _inner.DisposeAsync();
    }
}