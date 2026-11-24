namespace S7.NetCore.Internal;

/// <summary>
/// 字符串专属容量后缀的唯一规则源：允许类型、容量单位与范围、容量一致性。
/// </summary>
internal static class S7StringSuffixRules
{
    internal static bool TryGetAllowedDataType(S7AddressSuffix suffix, out S7DataType dataType)
    {
        switch (suffix)
        {
            case S7AddressSuffix.R:
                dataType = S7DataType.String;
                return true;
            case S7AddressSuffix.S:
                dataType = S7DataType.S7String;
                return true;
            case S7AddressSuffix.L:
                dataType = S7DataType.S7WString;
                return true;
            default:
                dataType = default;
                return false;
        }
    }

    internal static bool IsCapacitySuffix(S7AddressSuffix suffix)
    {
        return suffix is S7AddressSuffix.R or S7AddressSuffix.S or S7AddressSuffix.L;
    }

    internal static void ValidateAddressCapacity(
        S7AddressSuffix suffix,
        int capacity,
        string? expression)
    {
        if (!TryGetAllowedDataType(suffix, out S7DataType dataType))
        {
            throw CapacityInvalid(
                suffix,
                capacity,
                expression,
                "后缀不是字符串容量后缀");
        }

        S7WStringRules.GetCapacityLimits(dataType, out int minimum, out int maximum);
        if (capacity < minimum || capacity > maximum)
        {
            throw CapacityInvalid(
                suffix,
                capacity,
                expression,
                $"容量必须为 {minimum}～{maximum} {S7WStringRules.GetCapacityUnit(dataType)}");
        }
    }

    internal static int ResolveEffectiveCapacity(
        in S7Address address,
        int capacityParameter)
    {
        if (address.AddressCapacity is not int addressCapacity)
        {
            return capacityParameter;
        }

        if (capacityParameter != 0 && capacityParameter != addressCapacity)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StringCapacityInvalid,
                S7OperationStage.Validate,
                $"地址表达式 '{address.Expression}'，后缀 {address.Suffix}："
                + $"地址容量 {addressCapacity} 与容量参数 {capacityParameter} 不一致。");
        }

        ValidateAddressCapacity(address.Suffix, addressCapacity, address.Expression);
        return addressCapacity;
    }

    private static S7ClientException CapacityInvalid(
        S7AddressSuffix suffix,
        int capacity,
        string? expression,
        string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.StringCapacityInvalid,
            S7OperationStage.Validate,
            $"地址表达式 '{expression ?? "<null>"}'，后缀 {suffix}，容量 {capacity}：{reason}。");
    }
}