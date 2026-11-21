using System.Net;
using System.Net.Sockets;

namespace S7.NetCore.Internal;

/// <summary>
/// 基于 <see cref="Socket"/> 与 <see cref="NetworkStream"/> 的托管 TCP 传输实现。
/// </summary>
internal sealed class TcpTransport : ITcpTransport
{
    private Socket? _socket;
    private NetworkStream? _stream;
    private int _disposed;

    /// <inheritdoc />
    public bool IsOpen => _stream is not null && _socket is { Connected: true };

    /// <inheritdoc />
    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            throw new TransportException(S7ErrorCode.TcpConnectFailed, $"无法解析 IP 地址：{host}。");
        }

        if (_socket is not null)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "传输通道已处于打开状态。");
        }

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            await socket.ConnectAsync(new IPEndPoint(address, port), linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TransportException(
                S7ErrorCode.ConnectTimeout,
                $"连接 {host}:{port} 超时（{timeout.TotalSeconds} 秒）。",
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new TransportException(
                S7ErrorCode.TcpConnectFailed,
                $"连接 {host}:{port} 失败：{ex.Message}",
                ex);
        }

        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new TransportException(S7ErrorCode.ConnectionReset, "连接未建立。");

        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "读取过程中连接发生错误。", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new TransportException(S7ErrorCode.ConnectionReset, "连接未建立。");

        try
        {
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TransportException(S7ErrorCode.ConnectionReset, "写入过程中连接发生错误。", ex);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            _stream?.Dispose();
        }
        catch (IOException)
        {
        }

        try
        {
            _socket?.Dispose();
        }
        catch (SocketException)
        {
        }

        _stream = null;
        _socket = null;
        return ValueTask.CompletedTask;
    }
}