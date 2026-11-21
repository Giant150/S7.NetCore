using System.Diagnostics;

namespace S7.NetCore.Internal;

/// <summary>
/// 操作诊断作用域：记录操作开始时间并提供耗时。
/// </summary>
internal sealed class OperationScope
{
    private readonly long _startTimestamp;

    /// <summary>
    /// 初始化操作作用域。
    /// </summary>
    internal OperationScope()
    {
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>自作用域创建以来的耗时。</summary>
    internal TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);

    /// <summary>UTC 时间戳。</summary>
    internal static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}