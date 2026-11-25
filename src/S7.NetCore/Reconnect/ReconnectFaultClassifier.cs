namespace S7.NetCore.Internal;

/// <summary>
/// 判定一次故障是否满足自动重连条件。纯函数，不改变任何状态。
/// </summary>
internal static class ReconnectFaultClassifier
{
    /// <summary>
    /// 按固定顺序判定：开关启用 → 故障前状态为已连接 → 非调用方取消 → 仅连接类故障。
    /// 超时不属于连接类故障，永不返回 <see langword="true"/>。
    /// </summary>
    internal static bool ShouldReconnect(
        bool autoReconnectEnabled,
        S7SessionState stateBefore,
        bool callerCancelled,
        S7ErrorCategory category)
    {
        return autoReconnectEnabled
            && !callerCancelled
            && stateBefore == S7SessionState.Connected
            && category == S7ErrorCategory.Connection;
    }
}