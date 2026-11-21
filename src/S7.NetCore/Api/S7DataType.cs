namespace S7.NetCore;

/// <summary>
/// 支持的类型化数据类型。仅允许以下十一种，其他类型（DateTime、Struct、UDT、数组）不支持。
/// </summary>
public enum S7DataType
{
    /// <summary>布尔值，占用 1 字节载体（位语义为 1 位）。允许后缀 X 或非 DB 位形式。</summary>
    Bool = 1,

    /// <summary>无符号 8 位整数，范围 0～255，占用 1 字节。允许后缀 B。</summary>
    Byte = 2,

    /// <summary>无符号 16 位整数，范围 0～65535，占用 2 字节。允许后缀 W。</summary>
    Word = 3,

    /// <summary>有符号 16 位整数，范围 -32768～32767，占用 2 字节。允许后缀 W。</summary>
    Int = 4,

    /// <summary>无符号 32 位整数，范围 0～4294967295，占用 4 字节。允许后缀 D。</summary>
    DWord = 5,

    /// <summary>有符号 32 位整数，范围 -2147483648～2147483647，占用 4 字节。允许后缀 D。</summary>
    DInt = 6,

    /// <summary>IEEE754 单精度浮点数，占用 4 字节。允许后缀 D。</summary>
    Real = 7,

    /// <summary>IEEE754 双精度浮点数，占用 8 字节。允许后缀 D。</summary>
    LReal = 8,

    /// <summary>以 0x00 结尾的 ASCII C 字符串，占用 Capacity 字节，Capacity 范围 1～254。允许后缀 B。</summary>
    String = 9,

    /// <summary>带容量与实际长度头部的 ASCII 字符串，占用 Capacity + 2 字节，Capacity 范围 1～254。允许后缀 B。</summary>
    S7String = 10,

    /// <summary>
    /// UTF-16 双字节字符串：4 字节头（2 字节容量 + 2 字节长度，均大端、按 UTF-16 码元计）加 2 × Capacity 字节 UTF-16BE 字符区。
    /// Capacity 范围 1～16383 码元，占用 4 + 2 × Capacity 字节；无 0x00 终止符、无 BOM，未使用码元补 0x0000。允许后缀 B。
    /// </summary>
    S7WString = 11
}

/// <summary>
/// 地址表达式携带的访问宽度。
/// </summary>
public enum S7AccessWidth
{
    /// <summary>位访问，仅兼容 Bool。</summary>
    Bit = 0,

    /// <summary>字节访问，兼容 Byte、String、S7String、S7WString。</summary>
    Byte = 1,

    /// <summary>字访问，兼容 Word、Int。</summary>
    Word = 2,

    /// <summary>双字访问，兼容 DWord、DInt、Real、LReal。</summary>
    DWord = 3
}