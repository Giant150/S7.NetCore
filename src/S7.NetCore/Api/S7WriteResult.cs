namespace S7.NetCore;

/// <summary>
/// 字节写入结果及恢复元数据。
/// </summary>
/// <param name="BytesWritten">实际写入的字节数；成功时等于请求数据长度。</param>
/// <param name="RecoveryCount">本次调用中因重连恢复的次数，取值 0 或 1。</param>
/// <param name="Retried">是否在重连恢复后重试并成功。</param>
/// <param name="ReplayAttempted">是否因结果不确定而启用重放。</param>
/// <param name="ReplayedBytes">实际重放的字节数；未重放为 0。</param>
public readonly record struct S7WriteResult(
    int BytesWritten,
    int RecoveryCount,
    bool Retried,
    bool ReplayAttempted,
    int ReplayedBytes);