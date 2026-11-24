namespace S7.NetCore;

/// <summary>
/// 地址表达式中 DB 访问后缀的语义化标识。
/// <para>
/// <c>R</c>/<c>S</c>/<c>L</c> 仅限定字符串类型允许搭配的 <see cref="S7DataType"/>，不推导类型；
/// 调用方仍必须显式提供数据类型。
/// </para>
/// </summary>
public enum S7AddressSuffix : byte
{
    /// <summary>无 DB 宽度后缀（非 DB 地址的位形式或默认值）。</summary>
    None = 0,

    /// <summary>字节宽度 <c>DBB</c>。</summary>
    B = 1,

    /// <summary>字宽度 <c>DBW</c>。</summary>
    W = 2,

    /// <summary>双字宽度 <c>DBD</c>。</summary>
    D = 3,

    /// <summary>位宽度 <c>DBX</c>。</summary>
    X = 4,

    /// <summary>字符串容量后缀 <c>DBR</c>，仅允许 <see cref="S7DataType.String"/>，例如 <c>DB1.DBR13.15</c>。</summary>
    R = 5,

    /// <summary>字符串容量后缀 <c>DBS</c>，仅允许 <see cref="S7DataType.S7String"/>，例如 <c>DB1.DBS13.15</c>。</summary>
    S = 6,

    /// <summary>字符串容量后缀 <c>DBL</c>，仅允许 <see cref="S7DataType.S7WString"/>，例如 <c>DB1.DBL13.15</c>。</summary>
    L = 7
}