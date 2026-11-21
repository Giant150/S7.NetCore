namespace S7.NetCore.Internal;

/// <summary>
/// 类型规则：占用长度、地址宽度兼容性与占用范围。
/// </summary>
internal static class S7TypeRules
{
    internal static int GetOccupiedBytes(S7DataType dataType, int capacity)
    {
        return dataType switch
        {
            S7DataType.Bool or S7DataType.Byte => 1,
            S7DataType.Word or S7DataType.Int => 2,
            S7DataType.DWord or S7DataType.DInt or S7DataType.Real => 4,
            S7DataType.LReal => 8,
            S7DataType.String => ValidateStringCapacity(dataType, capacity),
            S7DataType.S7String => ValidateStringCapacity(dataType, capacity) + 2,
            S7DataType.S7WString => S7WStringRules.ValidateCapacity(dataType, capacity) * 2
                + S7WStringRules.HeaderSize,
            _ => throw DataTypeNotSupported(dataType)
        };
    }

    internal static bool IsCompatible(S7AccessWidth width, S7DataType dataType)
    {
        return width switch
        {
            S7AccessWidth.Bit => dataType == S7DataType.Bool,
            S7AccessWidth.Byte => dataType is S7DataType.Byte
                or S7DataType.String
                or S7DataType.S7String
                or S7DataType.S7WString,
            S7AccessWidth.Word => dataType is S7DataType.Word or S7DataType.Int,
            S7AccessWidth.DWord => dataType is S7DataType.DWord
                or S7DataType.DInt
                or S7DataType.Real
                or S7DataType.LReal,
            _ => false
        };
    }

    internal static void ValidateCompatibility(in S7Address address, S7DataType dataType)
    {
        if (!Enum.IsDefined(dataType))
        {
            throw DataTypeNotSupported(dataType);
        }

        if (!IsCompatible(address.Width, dataType))
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.DataTypeAddressMismatch,
                S7OperationStage.Validate,
                $"地址表达式 '{address.Expression}'，数据类型 {dataType}，访问宽度 {address.Width}：类型与地址宽度不兼容。"
                + "位地址仅支持 Bool；B 后缀支持 Byte/String/S7String/S7WString；W 后缀支持 Word/Int；D 后缀支持 DWord/DInt/Real/LReal。");
        }
    }

    internal static void ValidateOccupiedRange(in S7Address address, int bytesOccupied)
    {
        if (bytesOccupied < 1 || address.StartByte + bytesOccupied - 1 > 65535)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.AddressOutOfRange,
                S7OperationStage.Validate,
                $"地址表达式 '{address.Expression}'，起始字节 {address.StartByte}，占用 {bytesOccupied} 字节："
                + "超出区域容量上限 65535。");
        }
    }

    private static int ValidateStringCapacity(S7DataType dataType, int capacity)
    {
        return S7WStringRules.ValidateCapacity(dataType, capacity);
    }

    private static S7ClientException DataTypeNotSupported(S7DataType dataType)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.DataTypeNotSupported,
            S7OperationStage.Validate,
            $"数据类型 {(int)dataType} 不受支持：仅支持 Bool、Byte、Word、Int、DWord、DInt、Real、LReal、String、S7String、S7WString；DateTime、Struct、UDT、数组不支持。");
    }
}