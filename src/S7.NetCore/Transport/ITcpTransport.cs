namespace S7.NetCore;

/// <summary>
/// 传输层抽象。实现必须保证 <see cref="ReadAsync"/> 返回 0 仅表示对端关闭（EOF），
/// 并为 <see cref="WriteAsync"/> 提供完整写出语义。
/// </summary>
public interface ITcpTransport : IAsyncDisposable
{
    /// <summary>
    /// 建立 TCP 连接。
    /// </summary>
    /// <param name="host">目标 IPv4 或 IPv6 地址。</param>
    /// <param name="port">目标端口。</param>
    /// <param name="timeout">连接超时时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接完成的任务。</returns>
    Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// 读取数据。返回 0 表示对端已关闭连接。
    /// </summary>
    /// <param name="buffer">接收缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际读取的字节数。</returns>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// 完整写出数据。
    /// </summary>
    /// <param name="buffer">待写出的数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写出完成的任务。</returns>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>传输通道是否处于打开状态。</summary>
    bool IsOpen { get; }
}