using System.Reflection;

namespace S7.NetCore.Internal;

/// <summary>
/// 单个结构体字段的映射元数据与访问器。
/// </summary>
internal readonly record struct StructFieldDescriptor(
    string Name,
    Type FieldType,
    S7DataType DataType,
    int Offset,
    int Length,
    int Capacity,
    FieldInfo Field,
    Action<object, object?> Setter)
{
    internal object? GetValue(object instance)
    {
        return Field.GetValue(instance);
    }

    internal void SetValue(object instance, object? value)
    {
        try
        {
            Setter(instance, value);
        }
        catch (FieldAccessException)
        {
            Field.SetValue(instance, value);
        }
        catch (InvalidOperationException)
        {
            Field.SetValue(instance, value);
        }
    }
}

/// <summary>
/// 不可变结构体布局快照：类型名、总占用长度与按声明顺序排列的字段描述。
/// </summary>
internal sealed class StructLayoutSnapshot
{
    internal StructLayoutSnapshot(string typeName, int totalLength, IReadOnlyList<StructFieldDescriptor> fields)
    {
        TypeName = typeName;
        TotalLength = totalLength;
        Fields = fields;
    }

    internal string TypeName { get; }

    internal int TotalLength { get; }

    internal int FieldCount => Fields.Count;

    internal IReadOnlyList<StructFieldDescriptor> Fields { get; }
}