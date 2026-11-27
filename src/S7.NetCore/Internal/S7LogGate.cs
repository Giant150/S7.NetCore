namespace S7.NetCore.Internal;

/// <summary>
/// 日志值展示与协议跟踪的门控判定。所有格式化调用之前必须先经过本类判定。
/// </summary>
internal static class S7LogGate
{
    internal static bool ShouldWriteValue(IS7Logger logger, S7LogLevel level, bool valueTraceEnabled)
    {
        return valueTraceEnabled && logger.IsEnabled(level);
    }

    internal static bool ShouldTraceProtocol(IS7Logger logger, bool protocolTraceEnabled)
    {
        return protocolTraceEnabled && logger.IsEnabled(S7LogLevel.Trace);
    }
}