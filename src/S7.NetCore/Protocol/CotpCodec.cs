namespace S7.NetCore.Internal;

/// <summary>
/// COTP 连接请求构造、连接确认与数据单元识别。
/// </summary>
internal static class CotpCodec
{
    /// <summary>
    /// 构造 COTP 连接请求（CR）。
    /// </summary>
    /// <param name="local">本地 TSAP。</param>
    /// <param name="remote">远端 TSAP。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>完整 TPKT + COTP 帧。</returns>
    internal static byte[] BuildConnectionRequest(Tsap local, Tsap remote, S7OperationStage stage)
    {
        var frame = new byte[ProtocolConstants.CotpConnectionRequestSize];
        TpktCodec.WriteHeader(frame, frame.Length, stage);

        frame[4] = 0x11;
        frame[5] = 0xE0;
        frame[6] = 0x00;
        frame[7] = 0x00;
        frame[8] = 0x00;
        frame[9] = 0x01;
        frame[10] = 0x00;
        frame[11] = 0xC1;
        frame[12] = 0x02;
        frame[13] = local.FirstByte;
        frame[14] = local.SecondByte;
        frame[15] = 0xC2;
        frame[16] = 0x02;
        frame[17] = remote.FirstByte;
        frame[18] = remote.SecondByte;
        frame[19] = 0xC0;
        frame[20] = 0x01;
        frame[21] = 0x0A;

        return frame;
    }

    /// <summary>
    /// 判断完整帧是否为 COTP 连接确认（CC）。
    /// </summary>
    /// <param name="frame">完整 TPKT 帧。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>是连接确认返回 <see langword="true"/>。</returns>
    internal static bool IsConnectionConfirm(ReadOnlySpan<byte> frame, S7OperationStage stage)
    {
        if (frame.Length < ProtocolConstants.TpktHeaderSize + ProtocolConstants.CotpDataHeaderSize)
        {
            throw Error(S7ErrorCode.InvalidCotpFrame, stage, "COTP 连接确认帧长度不足。");
        }

        return frame[5] == ProtocolConstants.CotpPduTypeConnectionConfirm;
    }

    /// <summary>
    /// 读取 COTP DT 帧的分段信息。
    /// </summary>
    /// <param name="frame">完整 TPKT 帧。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>是否为最后分段。</returns>
    internal static bool IsLastDataUnit(ReadOnlySpan<byte> frame, S7OperationStage stage)
    {
        if (frame.Length < ProtocolConstants.TpktHeaderSize + ProtocolConstants.CotpDataHeaderSize)
        {
            throw Error(S7ErrorCode.InvalidCotpFrame, stage, "COTP 数据帧长度不足。");
        }

        if (frame[5] != ProtocolConstants.CotpPduTypeData)
        {
            throw Error(S7ErrorCode.InvalidCotpFrame, stage, $"COTP 报文类型非法：0x{frame[5]:X2}，期望 0xF0。");
        }

        return (frame[6] & ProtocolConstants.CotpLastDataUnit) != 0;
    }

    /// <summary>
    /// 从完整帧中截取 S7 净荷（跳过 TPKT 4 字节与 COTP 3 字节头）。
    /// </summary>
    /// <param name="frame">完整 TPKT 帧。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>S7 净荷。</returns>
    internal static ReadOnlySpan<byte> GetS7Payload(ReadOnlySpan<byte> frame, S7OperationStage stage)
    {
        int offset = ProtocolConstants.TpktHeaderSize + ProtocolConstants.CotpDataHeaderSize;
        if (frame.Length < offset)
        {
            throw Error(S7ErrorCode.InvalidCotpFrame, stage, "报文长度不足以包含 TPKT 与 COTP 头。");
        }

        return frame[offset..];
    }

    private static S7ClientException Error(S7ErrorCode code, S7OperationStage stage, string message)
    {
        return new S7ClientException(S7ErrorCategory.Protocol, code, stage, message);
    }
}