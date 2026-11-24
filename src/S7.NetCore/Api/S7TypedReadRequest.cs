namespace S7.NetCore;

/// <summary>
/// 类型化读取请求。
/// </summary>
/// <param name="Address">
/// 地址表达式，例如 <c>DB1.DBW0</c>、<c>DB1.DBR13.15</c>、<c>DB1.DBS13.15</c>、<c>DB1.DBL13.15</c>、<c>MW10</c>、<c>Q0.3</c>。
/// <c>DBR</c>/<c>DBS</c>/<c>DBL</c> 仅限定允许的数据类型，不会推导类型。
/// </param>
/// <param name="DataType">目标数据类型；仅支持十一种 S7 数据类型，必须显式提供（0 值返回 133）。</param>
/// <param name="Capacity">
/// 字符串容量：String/S7String 为 1～254（ASCII 字节），S7WString 为 1～16383（UTF-16 码元），其他类型必须为 0。
/// 地址已声明容量时，0 表示未提供并取地址容量；大于 0 必须与地址容量相等，否则返回 136。
/// </param>
public readonly record struct S7TypedReadRequest(string Address, S7DataType DataType, int Capacity = 0);