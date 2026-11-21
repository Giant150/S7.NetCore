namespace S7.NetCore.Internal;

/// <summary>
/// 退避时长计算。第 1 次尝试立即执行，其后按指数退避、上限与抖动计算。
/// </summary>
internal static class BackoffDelayCalculator
{
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 计算指定尝试序号前的退避时长。
    /// </summary>
    internal static TimeSpan Calculate(int attempt, S7ReconnectPolicy policy, Func<double>? random = null)
    {
        if (attempt <= 1)
        {
            return TimeSpan.Zero;
        }

        double baseMilliseconds = policy.InitialDelay.TotalMilliseconds
            * Math.Pow(policy.BackoffMultiplier, attempt - 2);
        baseMilliseconds = Math.Min(baseMilliseconds, policy.MaxDelay.TotalMilliseconds);

        double sample = (random ?? Random.Shared.NextDouble)();
        double jitter = (sample * 2.0) - 1.0;
        double value = baseMilliseconds * (1.0 + (policy.JitterFactor * jitter));

        double minimum = MinimumDelay.TotalMilliseconds;
        double maximum = Math.Max(minimum, policy.MaxDelay.TotalMilliseconds);
        value = Math.Clamp(value, minimum, maximum);

        return TimeSpan.FromMilliseconds(value);
    }
}