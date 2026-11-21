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

    /// <summary>是否启用断线自动重连。</summary>
    public bool AutoReconnectEnabled { get; init; }

    /// <summary>当前重连尝试序号；非重连状态为 0。</summary>
    public int ReconnectAttempt { get; init; }

    /// <summary>最近一次重连成功时间（UTC）；从未成功为 <see langword="null"/>。</summary>
    public DateTimeOffset? LastReconnectAt { get; init; }

    /// <summary>下一次重连尝试前的计划退避时长；非重连状态或无需等待为 <see langword="null"/>。</summary>
    public TimeSpan? NextReconnectDelay { get; init; }
}