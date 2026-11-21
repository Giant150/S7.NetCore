namespace S7.NetCore;

/// <summary>
/// 结构体读取结果。
/// </summary>
/// <typeparam name="T">已解码的结构体类型。</typeparam>
/// <param name="Value">解码后的结构体；未映射成员保持默认值。</param>
/// <param name="BytesOccupied">结构体映射字段的总占用长度。</param>
/// <param name="Address">解析后的字节地址；区域恒为 DB、宽度为 Byte、位号为 null。</param>
/// <param name="RecoveryCount">本次调用中因重连恢复的次数，取值 0 或 1。</param>
/// <param name="Retried">是否在重连恢复后重试并成功。</param>
public readonly record struct S7StructReadResult<T>(
    T Value,
    int BytesOccupied,
    S7Address Address,
    int RecoveryCount,
    bool Retried)
    where T : struct;