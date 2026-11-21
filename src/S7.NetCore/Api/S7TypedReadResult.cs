namespace S7.NetCore;

/// <summary>
/// 类型化读取结果。
/// </summary>
/// <param name="Value">解码后的值。</param>
/// <param name="BytesOccupied">该类型占用的字节数；Bool 的字节载体口径为 1。</param>
/// <param name="Address">解析后的地址。</param>
public readonly record struct S7TypedReadResult(S7Value Value, int BytesOccupied, S7Address Address);