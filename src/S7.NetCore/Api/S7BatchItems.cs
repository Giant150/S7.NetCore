namespace S7.NetCore;

/// <summary>
/// 批量字节读取条目。
/// </summary>
/// <param name="Area">存储区域，允许 Input、Output、Memory、DataBlock。</param>
/// <param name="DbNumber">数据块号；DB 区为 1～65535，其他区必须为 0。</param>
/// <param name="StartByte">起始字节地址，0～65535。</param>
/// <param name="Length">读取长度，1～65535。</param>
public readonly record struct S7BatchReadItem(S7Area Area, int DbNumber, int StartByte, int Length);

/// <summary>
/// 批量字节写入条目。
/// </summary>
/// <param name="Area">存储区域，允许 Output、Memory、DataBlock。</param>
/// <param name="DbNumber">数据块号；DB 区为 1～65535，其他区必须为 0。</param>
/// <param name="StartByte">起始字节地址，0～65535。</param>
/// <param name="Data">待写入数据，长度 1～65535；调用时复制为私有快照。</param>
public readonly record struct S7BatchWriteItem(
    S7Area Area,
    int DbNumber,
    int StartByte,
    ReadOnlyMemory<byte> Data);

/// <summary>
/// 批量类型化读取条目。
/// </summary>
/// <param name="Address">地址表达式，规则与单点类型化读取一致。</param>
/// <param name="DataType">十一种数据类型之一。</param>
/// <param name="Capacity">字符串容量；非字符串必须为 0。</param>
public readonly record struct S7TypedReadBatchItem(
    string Address,
    S7DataType DataType,
    int Capacity = 0);

/// <summary>
/// 批量类型化写入条目。
/// </summary>
/// <param name="Address">地址表达式，规则与单点类型化写入一致。</param>
/// <param name="DataType">十一种数据类型之一。</param>
/// <param name="Value">待写入值。</param>
/// <param name="Capacity">字符串容量；非字符串必须为 0。</param>
public readonly record struct S7TypedWriteBatchItem(
    string Address,
    S7DataType DataType,
    S7Value Value,
    int Capacity = 0);

/// <summary>
/// 批量操作失败条目明细。
/// </summary>
/// <param name="Index">请求中的条目索引，从 0 开始。</param>
/// <param name="ErrorCode">稳定错误码。</param>
/// <param name="PlcReturnCode">PLC 原始返回码；非 PLC 条目失败时为 <see langword="null"/>。</param>
public readonly record struct S7BatchItemFailure(
    int Index,
    S7ErrorCode ErrorCode,
    byte? PlcReturnCode);