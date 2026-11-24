namespace S7.NetCore.Internal;

/// <summary>
/// 结构体地址校验：仅允许 DBn.DBBm，且区间不得超出区域容量。
/// </summary>
internal static class StructAddressValidator
{
    internal static void Validate(in S7Address address, int totalLength)
    {
        if (address.Area != S7Area.DataBlock
            || address.Width != S7AccessWidth.Byte
            || address.Suffix != S7AddressSuffix.B
            || address.BitNumber is not null
            || address.DbNumber < 1)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StructAddressNotSupported,
                S7OperationStage.Validate,
                $"结构体地址 '{address.Expression}' 不受支持：仅接受 DBn.DBBm 字节地址。");
        }

        S7TypeRules.ValidateOccupiedRange(in address, totalLength);
    }
}