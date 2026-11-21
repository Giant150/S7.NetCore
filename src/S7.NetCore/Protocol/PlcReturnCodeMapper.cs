namespace S7.NetCore.Internal;

/// <summary>
/// PLC 返回码到稳定错误码的映射。
/// </summary>
internal static class PlcReturnCodeMapper
{
    /// <summary>
    /// 映射 PLC 返回码。0xFF 表示成功，不应传入本方法。
    /// </summary>
    /// <param name="returnCode">PLC 原始返回码。</param>
    /// <returns>错误码与可读消息。</returns>
    internal static (S7ErrorCode Code, string Message) Map(byte returnCode)
    {
        return returnCode switch
        {
            0x01 => (S7ErrorCode.PlcHardwareFault, "PLC 硬件故障"),
            0x03 => (S7ErrorCode.PlcAccessDenied, "PLC 不允许访问"),
            0x05 => (S7ErrorCode.PlcAddressOutOfRange, "PLC 地址越界"),
            0x06 => (S7ErrorCode.PlcDataTypeNotSupported, "PLC 数据类型不受支持"),
            0x07 => (S7ErrorCode.PlcDataInconsistent, "PLC 数据不一致"),
            0x0A => (S7ErrorCode.PlcObjectNotExist, "PLC 对象不存在"),
            _ => (S7ErrorCode.PlcUnknownReturnCode, $"PLC 返回未知返回码 0x{returnCode:X2}")
        };
    }
}