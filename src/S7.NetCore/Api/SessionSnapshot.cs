namespace S7.NetCore;

/// <summary>
/// 会话状态快照。
/// </summary>
public readonly record struct SessionSnapshot
{
    /// <summary>当前会话状态。</summary>
    public S7SessionState State { get; init; }

    /// <summary>当前会话标识；未连接时为 <see cref="Guid.Empty"/>。</summary>
    public Guid SessionId { get; init; }

    /// <summary>协商得到的 PDU 大小；未连接时为 0。</summary>
    public int NegotiatedPduSize { get; init; }
}