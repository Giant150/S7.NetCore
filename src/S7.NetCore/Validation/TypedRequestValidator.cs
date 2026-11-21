using S7.NetCore;

namespace S7.NetCore.Internal;

/// <summary>
/// 类型化请求校验器。所有非法输入在发起网络请求前失败。
/// </summary>
internal static class TypedRequestValidator
{
    internal static int ValidateRead(in S7TypedReadRequest request, in S7Address address)
    {
        return ValidateCommon(request.DataType, request.Capacity, address);
    }

    internal static int ValidateWrite(in S7TypedWriteRequest request, in S7Address address)
    {
        int occupied = ValidateCommon(request.DataType, request.Capacity, address);

        if (address.Area == S7Area.Input)
        {
            throw Invalid(
                S7ErrorCode.AreaNotSupported,
                request.DataType,
                address,
                occupied,
                "过程输入区不支持写入");
        }

        ValidateValue(request.DataType, request.Value, request.Capacity, address, occupied);
        return occupied;
    }

    private static int ValidateCommon(S7DataType dataType, int capacity, in S7Address address)
    {
        if (!Enum.IsDefined(dataType))
        {
            throw Invalid(S7ErrorCode.DataTypeNotSupported, dataType, address, 0, "数据类型不受支持");
        }

        bool isStringType = dataType is S7DataType.String
            or S7DataType.S7String
            or S7DataType.S7WString;
        if (isStringType)
        {
            S7WStringRules.GetCapacityLimits(dataType, out int minimum, out int maximum);
            if (capacity < minimum || capacity > maximum)
            {
                throw Invalid(
                    S7ErrorCode.StringCapacityInvalid,
                    dataType,
                    address,
                    0,
                    $"字符串容量 {capacity} 非法，必须为 {minimum}～{maximum} {S7WStringRules.GetCapacityUnit(dataType)}",
                    capacity);
            }
        }
        else if (capacity != 0)
        {
            throw Invalid(
                S7ErrorCode.StringCapacityInvalid,
                dataType,
                address,
                0,
                $"非字符串类型的容量必须为 0，实际为 {capacity}");
        }

        S7TypeRules.ValidateCompatibility(address, dataType);
        int occupied = S7TypeRules.GetOccupiedBytes(dataType, capacity);
        S7TypeRules.ValidateOccupiedRange(address, occupied);
        return occupied;
    }

    private static void ValidateValue(
        S7DataType dataType,
        in S7Value value,
        int capacity,
        in S7Address address,
        int occupied)
    {
        switch (dataType)
        {
            case S7DataType.Bool:
                if (!value.IsBoolean)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                break;

            case S7DataType.Byte:
                if (!value.IsInteger)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                if (!FitsUnsigned(value, byte.MaxValue))
                {
                    throw OutOfRange(dataType, address, occupied, "0～255");
                }

                break;

            case S7DataType.Word:
                if (!value.IsInteger)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                if (!FitsUnsigned(value, ushort.MaxValue))
                {
                    throw OutOfRange(dataType, address, occupied, "0～65535");
                }

                break;

            case S7DataType.Int:
                if (!value.IsInteger)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                if (!FitsSigned(value, short.MinValue, short.MaxValue))
                {
                    throw OutOfRange(dataType, address, occupied, "-32768～32767");
                }

                break;

            case S7DataType.DWord:
                if (!value.IsInteger)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                if (!FitsUnsigned(value, uint.MaxValue))
                {
                    throw OutOfRange(dataType, address, occupied, "0～4294967295");
                }

                break;

            case S7DataType.DInt:
                if (!value.IsInteger)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                if (!FitsSigned(value, int.MinValue, int.MaxValue))
                {
                    throw OutOfRange(dataType, address, occupied, "-2147483648～2147483647");
                }

                break;

            case S7DataType.Real:
                if (!value.IsSingle)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                break;

            case S7DataType.LReal:
                if (!value.IsSingle && !value.IsDouble)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                break;

            case S7DataType.String:
            case S7DataType.S7String:
                if (!value.IsString)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                ValidateStringValue(dataType, value.GetStringValue(), capacity, address, occupied);
                break;

            case S7DataType.S7WString:
                if (!value.IsString)
                {
                    throw TypeMismatch(dataType, address, occupied);
                }

                ValidateWStringValue(value.GetStringValue(), capacity, address, occupied);
                break;

            default:
                throw Invalid(S7ErrorCode.DataTypeNotSupported, dataType, address, occupied, "数据类型不受支持");
        }
    }

    private static void ValidateStringValue(
        S7DataType dataType,
        string text,
        int capacity,
        in S7Address address,
        int occupied)
    {
        foreach (char character in text)
        {
            if (character > 0x7F)
            {
                throw Invalid(
                    S7ErrorCode.StringEncodingInvalid,
                    dataType,
                    address,
                    occupied,
                    "字符串包含非 ASCII 字符，编码固定为 ASCII",
                    capacity);
            }
        }

        int maximumContentLength = dataType == S7DataType.String ? capacity - 1 : capacity;
        if (text.Length > maximumContentLength)
        {
            throw Invalid(
                S7ErrorCode.StringCapacityInvalid,
                dataType,
                address,
                occupied,
                $"字符串内容长度 {text.Length} 超过容量允许的最大长度 {maximumContentLength}",
                capacity);
        }
    }

    private static void ValidateWStringValue(
        string text,
        int capacity,
        in S7Address address,
        int occupied)
    {
        if (S7WStringRules.ContainsUnpairedSurrogate(text, out int index))
        {
            throw Invalid(
                S7ErrorCode.StringEncodingInvalid,
                S7DataType.S7WString,
                address,
                occupied,
                $"字符串包含孤立代理项，失败码元索引 {index}",
                capacity);
        }

        int maximum = S7WStringRules.GetMaxContentLength(S7DataType.S7WString, capacity);
        if (text.Length > maximum)
        {
            throw Invalid(
                S7ErrorCode.StringCapacityInvalid,
                S7DataType.S7WString,
                address,
                occupied,
                $"内容长度 {text.Length} 码元超过容量允许的最大长度 {maximum} 码元",
                capacity);
        }
    }

    private static bool FitsUnsigned(in S7Value value, ulong maximum)
    {
        if (value.IsSignedInteger)
        {
            long signed = value.GetSignedInteger();
            return signed >= 0 && (ulong)signed <= maximum;
        }

        return value.GetUnsignedInteger() <= maximum;
    }

    private static bool FitsSigned(in S7Value value, long minimum, long maximum)
    {
        if (value.IsSignedInteger)
        {
            long signed = value.GetSignedInteger();
            return signed >= minimum && signed <= maximum;
        }

        ulong unsigned = value.GetUnsignedInteger();
        return unsigned <= (ulong)maximum;
    }

    private static S7ClientException TypeMismatch(
        S7DataType dataType,
        in S7Address address,
        int occupied)
    {
        return Invalid(
            S7ErrorCode.ValueTypeMismatch,
            dataType,
            address,
            occupied,
            "值的种类与目标数据类型不匹配");
    }

    private static S7ClientException OutOfRange(
        S7DataType dataType,
        in S7Address address,
        int occupied,
        string range)
    {
        return Invalid(
            S7ErrorCode.ValueOutOfRange,
            dataType,
            address,
            occupied,
            $"写入值超出目标类型范围 {range}");
    }

    private static S7ClientException Invalid(
        S7ErrorCode code,
        S7DataType dataType,
        in S7Address address,
        int occupied,
        string reason,
        int capacity = -1)
    {
        string capacityText = capacity >= 0
            && dataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString
            ? $"，容量 {capacity}（{S7WStringRules.GetCapacityUnit(dataType)}）"
            : string.Empty;

        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            code,
            S7OperationStage.Validate,
            $"地址表达式 '{address.Expression}'，数据类型 {dataType}，区域 {address.Area}，DB {address.DbNumber}，"
            + $"起始字节 {address.StartByte}，位号 {address.BitNumber?.ToString() ?? "-"}，占用 {occupied} 字节{capacityText}：{reason}。");
    }
}