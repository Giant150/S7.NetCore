namespace S7.NetCore;

/// <summary>
/// 断线自动重连策略。默认关闭；客户端创建时固化并完成校验，运行期不可变。
/// </summary>
public sealed record S7ReconnectPolicy
{
    /// <summary>
    /// 默认策略：自动重连关闭，其余字段使用规格默认值。
    /// </summary>
    public static S7ReconnectPolicy Disabled { get; } = new();

    /// <summary>是否启用自动重连，默认 <see langword="false"/>。</summary>
    public bool AutoReconnectEnabled { get; init; }

    /// <summary>最大重连尝试次数，取值范围 0 至 100；0 表示不限次数，默认 5。</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>首次退避基准时长，取值范围 100 毫秒至 30 秒，默认 1 秒。</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>单次退避上限，取值范围 1 至 300 秒，且不得小于 <see cref="InitialDelay"/>，默认 30 秒。</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>退避倍数，取值范围 1.0 至 10.0，默认 2.0。</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>退避抖动比例，取值范围 0 至 0.5，默认 0.2。</summary>
    public double JitterFactor { get; init; } = 0.2;

    /// <summary>重连期间请求等待窗口，取值范围 0 至 120 秒；0 表示不等待并立即返回 <see cref="S7ErrorCode.SessionReconnecting"/>，默认 10 秒。</summary>
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>读写超时是否触发自动重连；默认 <see langword="false"/>，连接阶段超时始终可触发。</summary>
    public bool ReconnectOnTimeout { get; init; }

    /// <summary>结果不确定的写入是否允许从首个未确认分段起点重放；默认 <see langword="false"/>。</summary>
    public bool ReplayUncertainWrites { get; init; }
}