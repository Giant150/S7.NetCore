namespace S7.NetCore;

/// <summary>
/// 声明结构体中字符串字段的容量与 PLC 编码。容量单位：String/S7String 为 ASCII 字节，
/// S7WString 为 UTF-16 码元。构造函数不校验，非法值在结构体布局解析时统一报告。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class S7StringFieldAttribute : Attribute
{
    /// <summary>
    /// 创建字符串字段标注。
    /// </summary>
    /// <param name="capacity">字段容量。</param>
    public S7StringFieldAttribute(int capacity)
    {
        Capacity = capacity;
    }

    /// <summary>字段容量。</summary>
    public int Capacity { get; }

    /// <summary>PLC 字符串编码，仅允许 String、S7String 或 S7WString；默认 S7String。</summary>
    public S7DataType DataType { get; init; } = S7DataType.S7String;
}