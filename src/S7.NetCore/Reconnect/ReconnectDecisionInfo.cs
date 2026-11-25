namespace S7.NetCore.Internal;

/// <summary>
/// 重连快速失败判定信息。仅作为失败日志的关联异常参数，不新增公开成员。
/// </summary>
internal sealed class ReconnectDecisionInfo : Exception
{
    internal ReconnectDecisionInfo(
        string reason,
        TimeSpan elapsed,
        int bytesWritten = 0,
        bool writeOutcomeUncertain = false,
        Exception? innerException = null)
        : base(BuildMessage(reason, elapsed, bytesWritten, writeOutcomeUncertain), innerException)
    {
        Reason = reason;
        Elapsed = elapsed;
        BytesWritten = bytesWritten;
        WriteOutcomeUncertain = writeOutcomeUncertain;
    }

    internal string Reason { get; }

    internal TimeSpan Elapsed { get; }

    internal int BytesWritten { get; }

    internal bool WriteOutcomeUncertain { get; }

    private static string BuildMessage(
        string reason,
        TimeSpan elapsed,
        int bytesWritten,
        bool writeOutcomeUncertain)
    {
        return $"{reason}；判定耗时 {elapsed.TotalMilliseconds:F3} ms；"
            + $"已确认写入 {bytesWritten} 字节；写入结果不确定 {writeOutcomeUncertain}。";
    }
}