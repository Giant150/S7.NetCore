namespace S7.NetCore.Internal;

/// <summary>
/// PDU 协商结果。
/// </summary>
/// <param name="Requested">申请值。</param>
/// <param name="Confirmed">PLC 确认值。</param>
/// <param name="Effective">生效值。</param>
internal readonly record struct PduNegotiation(int Requested, int Confirmed, int Effective)
{
    /// <summary>单次读取净荷上限。</summary>
    internal int ReadPayloadLimit => Effective - ProtocolConstants.ReadResponseOverhead;

    /// <summary>单次写入净荷上限。</summary>
    internal int WritePayloadLimit => Effective - ProtocolConstants.WriteRequestOverhead;
}