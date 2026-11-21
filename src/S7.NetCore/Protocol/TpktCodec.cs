using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// TPKT 帧头编解码。
/// </summary>
internal static class TpktCodec
{
    /// <summary>
    /// 写入 TPKT 帧头。
    /// </summary>
    /// <param name="destination">目标缓冲区，长度至少 4 字节。</param>
    /// <param name="totalLength">包含帧头在内的整帧长度。</param>
    /// <param name="stage">失败阶段。</param>
    internal static void WriteHeader(Span<byte> destination, int totalLength, S7OperationStage stage)
    {
        if (totalLength is < 7 or > ushort.MaxValue)
        {
            throw Error(S7ErrorCode.InvalidFrame, stage, $"TPKT 帧长度非法：{totalLength}。");
        }

        destination[0] = ProtocolConstants.TpktVersion;
        destination[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)totalLength);
    }

    /// <summary>
    /// 解析 TPKT 帧头并返回整帧长度。
    /// </summary>
    /// <param name="header">帧头缓冲区，长度至少 4 字节。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>整帧长度。</returns>
    internal static int ReadLength(ReadOnlySpan<byte> header, S7OperationStage stage)
    {
        if (header.Length < ProtocolConstants.TpktHeaderSize)
        {
            throw Error(S7ErrorCode.InvalidFrame, stage, "TPKT 帧头长度不足 4 字节。");
        }

        if (header[0] != ProtocolConstants.TpktVersion)
        {
            throw Error(S7ErrorCode.InvalidFrame, stage, $"TPKT 版本非法：0x{header[0]:X2}，期望 0x03。");
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(header[2..]);
        if (length < ProtocolConstants.TpktHeaderSize + ProtocolConstants.CotpDataHeaderSize)
        {
            throw Error(S7ErrorCode.InvalidFrame, stage, $"TPKT 长度非法：{length}，至少应包含 TPKT 与 COTP 头。");
        }

        if (length > ProtocolConstants.MaxFrameSize)
        {
            throw Error(S7ErrorCode.InvalidFrame, stage, $"TPKT 长度 {length} 超过最大帧长 {ProtocolConstants.MaxFrameSize}。");
        }

        return length;
    }

    private static S7ClientException Error(S7ErrorCode code, S7OperationStage stage, string message)
    {
        return new S7ClientException(S7ErrorCategory.Protocol, code, stage, message);
    }
}