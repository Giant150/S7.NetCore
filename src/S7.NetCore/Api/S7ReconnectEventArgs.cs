namespace S7.NetCore;

/// <summary>
/// 重连生命周期事件类型。
/// </summary>
public enum S7ReconnectEventKind
{
    /// <summary>某次重连尝试开始。</summary>
    AttemptStarted = 0,

    /// <summary>某次重连尝试失败。</summary>
    AttemptFailed = 1,

    /// <summary>重连成功并恢复为已连接。</summary>
    Reconnected = 2,

    /// <summary>重连次数耗尽。</summary>
    Exhausted = 3,

    /// <summary>重连被调用方断开或客户端释放中止。</summary>
    Aborted = 4
}

/// <summary>
/// 重连中止原因。
/// </summary>
public enum S7ReconnectAbortReason
{
    /// <summary>调用方显式断开。</summary>
    DisconnectedByCaller = 0,

    /// <summary>客户端被释放。</summary>
    ClientDisposed = 1
}

/// <summary>
/// 重连生命周期事件。仅承载元数据，不包含读写数据。
/// </summary>
public sealed class S7ReconnectEventArgs : EventArgs
{
    /// <summary>事件类型。</summary>
    public S7ReconnectEventKind Kind { get; init; }

    /// <summary>当前尝试序号；重连成功或耗尽时为最后一次尝试序号。</summary>
    public int Attempt { get; init; }

    /// <summary>最大尝试次数；0 表示不限。</summary>
    public int MaxAttempts { get; init; }

    /// <summary>本次尝试前的退避时长。</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>从首次断线到事件产生的时长。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>失败类别；仅失败与耗尽事件有值。</summary>
    public S7ErrorCategory? ErrorCategory { get; init; }

    /// <summary>失败错误码；仅失败与耗尽事件有值。</summary>
    public S7ErrorCode? ErrorCode { get; init; }

    /// <summary>断线持续时长；仅成功与耗尽事件有值。</summary>
    public TimeSpan? DisconnectedDuration { get; init; }

    /// <summary>重连成功后的新会话标识；仅成功事件有值。</summary>
    public Guid? NewSessionId { get; init; }

    /// <summary>重连成功后的协商 PDU；仅成功事件有值。</summary>
    public int? NegotiatedPduSize { get; init; }

    /// <summary>中止原因；仅中止事件有值。</summary>
    public S7ReconnectAbortReason? AbortReason { get; init; }
}