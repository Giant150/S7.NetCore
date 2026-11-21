namespace S7.NetCore.Internal;

/// <summary>
/// 判定一次故障是否满足自动重连条件。纯函数，不改变任何状态。
/// </summary>
internal static class ReconnectFaultClassifier
{
    /// <summary>
    /// 按固定顺序判定：策略启用 → 故障前状态为已连接 → 非调用方取消 → 仅连接类故障或允许的超时。
    /// </summary>
    internal static bool ShouldReconnect(
        S7ReconnectPolicy policy,
        S7SessionState stateBefore,
        bool callerCancelled,
        S7ErrorCategory category,
        S7OperationStage stage)
    {
        if (!policy.AutoReconnectEnabled || callerCancelled || stateBefore != S7SessionState.Connected)
        {
            return false;
        }

        if (category == S7ErrorCategory.Connection)
        {
            return true;
        }

        if (category == S7ErrorCategory.Timeout)
        {
            return policy.ReconnectOnTimeout
                || stage is S7OperationStage.Connect or S7OperationStage.CotpConnect or S7OperationStage.S7Setup;
        }

        return false;
    }
}