namespace S7.NetCore.Internal;

/// <summary>
/// 退避时长计算。首次尝试立即执行，其后固定为 1s、2s、4s…，上限 30s，无抖动。
/// </summary>
internal static class BackoffDelayCalculator
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 计算指定尝试序号前的退避时长，序列为 0, 1s, 2s, 4s, 8s, 16s, 30s, 30s…
    /// </summary>
    internal static TimeSpan Calculate(int attempt)
    {
        if (attempt <= 1)
        {
            return TimeSpan.Zero;
        }

        double milliseconds = 1000.0 * Math.Pow(2, attempt - 2);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaximumDelay.TotalMilliseconds));
    }
}