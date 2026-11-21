namespace S7.NetCore;

/// <summary>
/// 字节读取结果及恢复元数据。
/// </summary>
/// <param name="Data">与请求长度一致的原始字节序列。</param>
/// <param name="RecoveryCount">本次调用中因重连恢复的次数，取值 0 或 1。</param>
/// <param name="Retried">是否在重连恢复后重试并成功。</param>
public readonly record struct S7ReadResult(
    ReadOnlyMemory<byte> Data,
    int RecoveryCount,
    bool Retried);