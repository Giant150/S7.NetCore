namespace S7.NetCore.Internal;

/// <summary>
/// PDU 大小协商：钳制在 [240, 申请值]，缺失或非法时回退 240。
/// </summary>
internal static class PduSizeNegotiator
{
    /// <summary>
    /// 计算协商结果。
    /// </summary>
    /// <param name="requested">申请值。</param>
    /// <param name="confirmed">PLC 确认值；0 表示缺失。</param>
    /// <returns>协商结果。</returns>
    internal static PduNegotiation Negotiate(int requested, int confirmed)
    {
        int effective = confirmed <= 0
            ? ProtocolConstants.DefaultPduSize
            : Math.Clamp(confirmed, ProtocolConstants.MinPduSize, requested);

        return new PduNegotiation(requested, confirmed, effective);
    }
}