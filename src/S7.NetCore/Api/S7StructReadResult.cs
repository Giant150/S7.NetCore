namespace S7.NetCore;

/// <summary>
/// 结构体读取结果。
/// </summary>
/// <typeparam name="T">已解码的结构体类型。</typeparam>
/// <param name="Value">解码后的结构体；未映射成员保持默认值。</param>
/// <param name="BytesOccupied">结构体映射字段的总占用长度。</param>
/// <param name="Address">解析后的字节地址；区域恒为 DB、宽度为 Byte、位号为 null。</param>
public readonly record struct S7StructReadResult<T>(
    T Value,
    int BytesOccupied,
    S7Address Address)
    where T : struct;