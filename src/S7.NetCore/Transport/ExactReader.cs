namespace S7.NetCore.Internal;

/// <summary>
/// 精确读取原语：读满指定长度，0 字节视为 EOF。
/// </summary>
internal static class ExactReader
{
    /// <summary>
    /// 读满缓冲区。
    /// </summary>
    /// <param name="transport">传输通道。</param>
    /// <param name="buffer">接收缓冲区。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="TransportException">连接提前关闭时抛出。</exception>
    internal static async ValueTask ReadExactAsync(
        ITcpTransport transport,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await transport.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new TransportException(
                    S7ErrorCode.ConnectionReset,
                    $"连接在读取完成前关闭：期望 {buffer.Length} 字节，已读取 {offset} 字节。");
            }

            offset += read;
        }
    }
}