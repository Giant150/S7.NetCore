namespace S7.NetCore;

/// <summary>
/// 字节读取请求。为不可变值类型，不包含地址字符串解析。
/// </summary>
/// <param name="Area">存储区域，仅允许 <see cref="S7Area.Input"/>、<see cref="S7Area.Output"/>、<see cref="S7Area.Memory"/>、<see cref="S7Area.DataBlock"/>。</param>
/// <param name="DbNumber">数据块号；数据块区必填 1 至 65535，其他区域必须为 0。</param>
/// <param name="StartByte">起始字节地址，取值范围 0 至 65535。</param>
/// <param name="Length">读取字节长度，取值范围 1 至 65535。</param>
public readonly record struct S7ReadRequest(S7Area Area, int DbNumber, int StartByte, int Length);