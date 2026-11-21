namespace S7.NetCore;

/// <summary>
/// 类型化写入请求。
/// </summary>
/// <param name="Address">地址表达式，例如 <c>DB2.DBW0</c>、<c>MW10</c>、<c>Q0.3</c>。</param>
/// <param name="DataType">目标数据类型；仅支持十一种 S7 数据类型。</param>
/// <param name="Value">待写入值；整数族允许宽化，Real 仅接受 <see cref="S7Value.FromSingle"/>，LReal 接受 Single/Double。</param>
/// <param name="Capacity">字符串容量：String/S7String 必填 1～254（ASCII 字节），S7WString 必填 1～16383（UTF-16 码元），其他类型必须为 0。</param>
public readonly record struct S7TypedWriteRequest(string Address, S7DataType DataType, S7Value Value, int Capacity = 0);