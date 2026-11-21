namespace S7.NetCore.Internal;

/// <summary>
/// 日志安全包装器：吞掉日志设施异常，保证主流程不受影响。
/// </summary>
internal sealed class SafeS7Logger : IS7Logger
{
    private readonly IS7Logger _inner;

    /// <summary>
    /// 初始化安全包装器。
    /// </summary>
    /// <param name="inner">被包装的日志设施。</param>
    internal SafeS7Logger(IS7Logger inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public bool IsEnabled(S7LogLevel level)
    {
        try
        {
            return _inner.IsEnabled(level);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
        try
        {
            _inner.Log(level, in entry, exception);
        }
        catch
        {
        }
    }
}