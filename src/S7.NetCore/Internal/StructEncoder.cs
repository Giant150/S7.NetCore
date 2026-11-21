using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// 结构体整段编码：先完成全部字段校验与编码，再返回字节数组。
/// </summary>
internal static class StructEncoder
{
    internal static byte[] Encode<T>(in T value, StructLayoutSnapshot snapshot)
        where T : struct
    {
        object boxed = value;
        var data = new byte[snapshot.TotalLength];

        foreach (StructFieldDescriptor field in snapshot.Fields)
        {
            object? fieldValue = field.GetValue(boxed);
            EncodeField(fieldValue, field, data.AsSpan(field.Offset, field.Length), snapshot.TypeName);
        }

        return data;
    }

    internal static void EncodeField(
        object? boxed,
        in StructFieldDescriptor field,
        Span<byte> destination,
        string structTypeName)
    {
        try
        {
            switch (field.DataType)
            {
                case S7DataType.Bool:
                    if (boxed is not bool boolean)
                    {
                        throw TypeMismatch(structTypeName, field);
                    }

                    destination[0] = boolean ? (byte)1 : (byte)0;
                    break;

                case S7DataType.Byte:
                    destination[0] = (byte)GetInteger(boxed, field, structTypeName, 0, byte.MaxValue);
                    break;

                case S7DataType.Int:
                    BinaryPrimitives.WriteUInt16BigEndian(
                        destination,
                        unchecked((ushort)(short)GetInteger(boxed, field, structTypeName, short.MinValue, short.MaxValue)));
                    break;

                case S7DataType.Word:
                    BinaryPrimitives.WriteUInt16BigEndian(
                        destination,
                        (ushort)GetInteger(boxed, field, structTypeName, 0, ushort.MaxValue));
                    break;

                case S7DataType.DInt:
                    BinaryPrimitives.WriteUInt32BigEndian(
                        destination,
                        unchecked((uint)(int)GetInteger(boxed, field, structTypeName, int.MinValue, int.MaxValue)));
                    break;

                case S7DataType.DWord:
                    BinaryPrimitives.WriteUInt32BigEndian(
                        destination,
                        (uint)GetInteger(boxed, field, structTypeName, 0, uint.MaxValue));
                    break;

                case S7DataType.Real:
                    if (boxed is not float single)
                    {
                        throw TypeMismatch(structTypeName, field);
                    }

                    BinaryPrimitives.WriteUInt32BigEndian(destination, BitConverter.SingleToUInt32Bits(single));
                    break;

                case S7DataType.LReal:
                    double doubleValue = boxed switch
                    {
                        double value => value,
                        float value => value,
                        _ => throw TypeMismatch(structTypeName, field)
                    };
                    BinaryPrimitives.WriteUInt64BigEndian(
                        destination,
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(doubleValue)));
                    break;

                case S7DataType.String:
                case S7DataType.S7String:
                case S7DataType.S7WString:
                    string text = boxed as string ?? string.Empty;
                    byte[] encoded = S7ValueCodec.Encode(field.DataType, S7Value.FromString(text), field.Capacity);
                    encoded.CopyTo(destination);
                    break;

                default:
                    throw TypeMismatch(structTypeName, field);
            }
        }
        catch (S7ClientException exception) when (
            exception.ErrorCode is S7ErrorCode.ValueTypeMismatch
                or S7ErrorCode.ValueOutOfRange
                or S7ErrorCode.StringEncodingInvalid
                or S7ErrorCode.StringCapacityInvalid)
        {
            throw exception.WithContext(
                $"结构体 '{structTypeName}' 字段 '{field.Name}'（{field.DataType}，容量 {field.Capacity}，偏移 {field.Offset}，长度 {field.Length}）。");
        }
    }

    private static long GetInteger(
        object? value,
        in StructFieldDescriptor field,
        string structTypeName,
        long minimum,
        long maximum)
    {
        long signed = value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v when v <= long.MaxValue => (long)v,
            _ => throw TypeMismatch(structTypeName, field)
        };

        if (signed < minimum || signed > maximum)
        {
            throw OutOfRange(structTypeName, field, minimum, maximum);
        }

        return signed;
    }

    private static S7ClientException TypeMismatch(string structTypeName, in StructFieldDescriptor field)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.ValueTypeMismatch,
            S7OperationStage.Validate,
            $"结构体 '{structTypeName}' 字段 '{field.Name}' 的值类型与 {field.DataType} 不匹配。");
    }

    private static S7ClientException OutOfRange(
        string structTypeName,
        in StructFieldDescriptor field,
        long minimum,
        long maximum)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.ValueOutOfRange,
            S7OperationStage.Validate,
            $"结构体 '{structTypeName}' 字段 '{field.Name}' 的值超出 {field.DataType} 允许范围 {minimum}～{maximum}。");
    }
}