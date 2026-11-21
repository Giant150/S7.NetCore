using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// 结构体整段解码：任一字段失败即整体失败，不返回部分字段正确的实例。
/// </summary>
internal static class StructDecoder
{
    internal static T Decode<T>(ReadOnlySpan<byte> data, StructLayoutSnapshot snapshot)
        where T : struct
    {
        if (data.Length < snapshot.TotalLength)
        {
            throw new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"结构体 '{snapshot.TypeName}' 解码失败：需要至少 {snapshot.TotalLength} 字节，实际 {data.Length} 字节。");
        }

        object boxed = default(T)!;

        foreach (StructFieldDescriptor field in snapshot.Fields)
        {
            try
            {
                object? value = DecodeField(field, data.Slice(field.Offset, field.Length));
                field.SetValue(boxed, value);
            }
            catch (S7ClientException exception) when (exception.Stage == S7OperationStage.Read)
            {
                throw exception.WithContext(
                    $"结构体 '{snapshot.TypeName}' 字段 '{field.Name}'（{field.DataType}，容量 {field.Capacity}，偏移 {field.Offset}，长度 {field.Length}）。");
            }
        }

        return (T)boxed;
    }

    private static object? DecodeField(in StructFieldDescriptor field, ReadOnlySpan<byte> data)
    {
        return field.DataType switch
        {
            S7DataType.Bool => data[0] != 0,
            S7DataType.Byte => data[0],
            S7DataType.Int => BinaryPrimitives.ReadInt16BigEndian(data),
            S7DataType.Word => BinaryPrimitives.ReadUInt16BigEndian(data),
            S7DataType.DInt => BinaryPrimitives.ReadInt32BigEndian(data),
            S7DataType.DWord => BinaryPrimitives.ReadUInt32BigEndian(data),
            S7DataType.Real => BitConverter.UInt32BitsToSingle(BinaryPrimitives.ReadUInt32BigEndian(data)),
            S7DataType.LReal => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data)),
            S7DataType.String or S7DataType.S7String or S7DataType.S7WString =>
                S7ValueCodec.Decode(field.DataType, data, field.Capacity).AsString(),
            _ => throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.DataTypeNotSupported,
                S7OperationStage.Validate,
                $"数据类型 {field.DataType} 不受支持。")
        };
    }
}