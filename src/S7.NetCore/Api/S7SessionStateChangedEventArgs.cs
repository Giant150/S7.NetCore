namespace S7.NetCore;

/// <summary>
/// 会话状态迁移通知。仅承载状态与重连进度元数据，不包含读写数据。
/// </summary>
public sealed class S7SessionStateChangedEventArgs : EventArgs
{
    /// <summary>迁移前状态。</summary>
    public S7SessionState PreviousState { get; init; }

    /// <summary>迁移后状态。</summary>
    public S7SessionState CurrentState { get; init; }

    /// <summary>当前会话标识；未连接或重连中为 <see cref="Guid.Empty"/>。</summary>
    public Guid SessionId { get; init; }

    /// <summary>当前重连尝试序号；非重连迁移为 <see langword="null"/>。</summary>
    public int? Attempt { get; init; }

    /// <summary>触发本次重连的首次故障错误码；非重连迁移为 <see langword="null"/>。</summary>
    public S7ErrorCode? FirstFaultErrorCode { get; init; }

    /// <summary>事件产生时间（UTC）。</summary>
    public DateTimeOffset Timestamp { get; init; }
}