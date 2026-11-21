namespace S7.NetCore;

/// <summary>
/// 字节写入请求。为不可变值类型，写入数据以只读内存切片传递，避免整包复制。
/// </summary>
/// <param name="Area">存储区域，仅允许 <see cref="S7Area.Output"/>、<see cref="S7Area.Memory"/>、<see cref="S7Area.DataBlock"/>。</param>
/// <param name="DbNumber">数据块号；数据块区必填 1 至 65535，其他区域必须为 0。</param>
/// <param name="StartByte">起始字节地址，取值范围 0 至 65535。</param>
/// <param name="Data">待写入字节序列，长度取值范围 1 至 65535，写入顺序与序列顺序一致。</param>
public readonly record struct S7WriteRequest(S7Area Area, int DbNumber, int StartByte, ReadOnlyMemory<byte> Data);