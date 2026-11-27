namespace S7.NetCore.Internal;

/// <summary>
/// 值格式化结果载体：文本、格式、截断标记、展示条目数与降级标记。
/// </summary>
internal readonly record struct S7ValueText
{
    internal string? Text { get; init; }

    internal S7ValueFormat Format { get; init; }

    internal bool Truncated { get; init; }

    internal int? ItemCount { get; init; }

    internal bool FormatFailed { get; init; }

    internal static S7ValueText None => default;
}