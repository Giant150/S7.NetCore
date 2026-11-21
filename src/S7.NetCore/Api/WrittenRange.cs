namespace S7.NetCore;

/// <summary>
/// 已写入的数据区间。
/// </summary>
/// <param name="StartByte">已写入区间的起始字节地址。</param>
/// <param name="Length">已写入的字节数。</param>
public readonly record struct WrittenRange(int StartByte, int Length);