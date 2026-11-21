using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace S7.NetCore.Internal;

/// <summary>
/// 解析结构体布局并按类型缓存。失败不写入缓存，修复类型后可重新解析。
/// </summary>
internal static class StructLayoutResolver
{
    internal static StructLayoutSnapshot Resolve<T>()
        where T : struct
    {
        StructLayoutSnapshot? cached = Cache<T>.Snapshot;
        if (cached is not null)
        {
            return cached;
        }

        lock (Cache<T>.Sync)
        {
            cached = Cache<T>.Snapshot;
            if (cached is null)
            {
                cached = Build(typeof(T));
                Cache<T>.Snapshot = cached;
            }
        }

        return cached;
    }

    private static class Cache<T>
        where T : struct
    {
        internal static readonly object Sync = new();

        internal static StructLayoutSnapshot? Snapshot;
    }

    private static StructLayoutSnapshot Build(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not null || type.IsEnum)
        {
            throw FieldTypeNotSupported(type.Name, type.Name, type);
        }

        if (type.StructLayoutAttribute?.Value == LayoutKind.Explicit)
        {
            throw LayoutInvalid(type.Name, "不支持 LayoutKind.Explicit 显式布局。");
        }

        FieldInfo[] mappedFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(static field => field.MetadataToken)
            .ToArray();

        if (mappedFields.Length == 0)
        {
            throw LayoutInvalid(type.Name, "映射字段数为 0，仅支持公共实例字段。");
        }

        var mappedSet = new HashSet<int>(mappedFields.Select(static field => field.MetadataToken));
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            int annotationCount = field.GetCustomAttributes<S7StringFieldAttribute>(inherit: false).Count();
            if (annotationCount == 0)
            {
                continue;
            }

            if (!mappedSet.Contains(field.MetadataToken))
            {
                throw LayoutInvalid(type.Name, $"字段 '{field.Name}' 带有字符串标注但不参与映射，仅公共实例字段可映射。");
            }

            if (annotationCount > 1)
            {
                throw LayoutInvalid(type.Name, $"字段 '{field.Name}' 重复声明字符串容量。");
            }
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (property.GetCustomAttributes<S7StringFieldAttribute>(inherit: false).Any())
            {
                throw LayoutInvalid(type.Name, $"属性 '{property.Name}' 不支持字符串标注，仅公共实例字段可映射。");
            }
        }

        var fields = new List<StructFieldDescriptor>(mappedFields.Length);
        int offset = 0;

        foreach (FieldInfo field in mappedFields)
        {
            if (field.GetCustomAttribute<FieldOffsetAttribute>(inherit: false) is not null)
            {
                throw LayoutInvalid(type.Name, $"字段 '{field.Name}' 使用了 FieldOffset，结构体读写不支持显式字节偏移。");
            }

            S7StringFieldAttribute[] annotations = field
                .GetCustomAttributes<S7StringFieldAttribute>(inherit: false)
                .ToArray();
            Type fieldType = field.FieldType;
            S7DataType dataType;
            int capacity = 0;
            int length;

            if (fieldType == typeof(string))
            {
                if (annotations.Length == 0)
                {
                    throw CapacityMissing(type.Name, field.Name);
                }

                S7StringFieldAttribute annotation = annotations[0];
                dataType = annotation.DataType;
                if (dataType is not (S7DataType.String or S7DataType.S7String or S7DataType.S7WString))
                {
                    throw LayoutInvalid(type.Name, $"字段 '{field.Name}' 的字符串编码 {dataType} 非法，仅允许 String、S7String 或 S7WString。");
                }

                capacity = annotation.Capacity;
                S7WStringRules.GetCapacityLimits(dataType, out int minimum, out int maximum);
                if (capacity < minimum || capacity > maximum)
                {
                    throw LayoutInvalid(
                        type.Name,
                        $"字段 '{field.Name}' 的容量 {capacity} 超出 {minimum}～{maximum} {S7WStringRules.GetCapacityUnit(dataType)}。");
                }

                length = S7TypeRules.GetOccupiedBytes(dataType, capacity);
            }
            else
            {
                if (annotations.Length > 0)
                {
                    throw LayoutInvalid(type.Name, $"非字符串字段 '{field.Name}' 不允许字符串容量标注。");
                }

                dataType = fieldType switch
                {
                    _ when fieldType == typeof(bool) => S7DataType.Bool,
                    _ when fieldType == typeof(byte) => S7DataType.Byte,
                    _ when fieldType == typeof(short) => S7DataType.Int,
                    _ when fieldType == typeof(ushort) => S7DataType.Word,
                    _ when fieldType == typeof(int) => S7DataType.DInt,
                    _ when fieldType == typeof(uint) => S7DataType.DWord,
                    _ when fieldType == typeof(float) => S7DataType.Real,
                    _ when fieldType == typeof(double) => S7DataType.LReal,
                    _ => throw FieldTypeNotSupported(type.Name, field.Name, fieldType)
                };
                length = S7TypeRules.GetOccupiedBytes(dataType, 0);
            }

            fields.Add(new StructFieldDescriptor(
                field.Name,
                fieldType,
                dataType,
                offset,
                length,
                capacity,
                field,
                CreateSetter(field)));
            offset += length;
        }

        if (fields.Count == 0)
        {
            throw LayoutInvalid(type.Name, "映射字段数为 0，仅支持公共实例字段。");
        }

        if (offset is < 1 or > 65535)
        {
            throw LayoutInvalid(type.Name, $"总占用长度 {offset} 超出 1～65535。");
        }

        return new StructLayoutSnapshot(type.Name, offset, fields);
    }

    private static Action<object, object?> CreateSetter(FieldInfo field)
    {
        try
        {
            var method = new DynamicMethod(
                "S7StructSet_" + field.DeclaringType!.Name + "_" + field.Name,
                typeof(void),
                [typeof(object), typeof(object)],
                field.DeclaringType,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox, field.DeclaringType!);
            il.Emit(OpCodes.Ldarg_1);
            if (field.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, field.FieldType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, field.FieldType);
            }

            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            return (Action<object, object?>)method.CreateDelegate(typeof(Action<object, object?>));
        }
        catch (Exception)
        {
            return (instance, value) => field.SetValue(instance, value);
        }
    }

    private static S7ClientException LayoutInvalid(string typeName, string detail)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.StructLayoutInvalid,
            S7OperationStage.Validate,
            $"结构体 '{typeName}' 布局非法：{detail}");
    }

    private static S7ClientException CapacityMissing(string typeName, string fieldName)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.StringCapacityInvalid,
            S7OperationStage.Validate,
            $"结构体 '{typeName}' 的字符串字段 '{fieldName}' 未声明容量，请使用 [S7StringField(capacity)] 标注。");
    }

    private static S7ClientException FieldTypeNotSupported(string typeName, string fieldName, Type fieldType)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.DataTypeNotSupported,
            S7OperationStage.Validate,
            $"结构体 '{typeName}' 字段 '{fieldName}' 的类型 {fieldType.Name} 不受支持："
            + "仅支持 bool、byte、short、ushort、int、uint、float、double、string。");
    }
}