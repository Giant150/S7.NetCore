namespace S7.NetCore;

/// <summary>
/// 会话状态迁移通知。仅承载状态迁移元数据，不包含读写数据。
/// </summary>
public sealed class S7SessionStateChangedEventArgs : EventArgs
{
    /// <summary>迁移前状态。</summary>
    public S7SessionState PreviousState { get; init; }

    /// <summary>迁移后状态。</summary>
    public S7SessionState CurrentState { get; init; }

    /// <summary>当前会话标识；未连接或重连中为 <see cref="Guid.Empty"/>。</summary>
    public Guid SessionId { get; init; }

    /// <summary>事件产生时间（UTC）。</summary>
    public DateTimeOffset Timestamp { get; init; }
}