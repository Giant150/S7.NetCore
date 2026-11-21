using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// S7 Ack_Data 响应头。
/// </summary>
internal readonly record struct S7AckHeader(ushort PduReference, int ParameterLength, int DataLength);

/// <summary>
/// 读取响应解析结果。
/// </summary>
internal readonly record struct ReadResponseInfo(byte ReturnCode, int DataOffset, int DataLength);

/// <summary>
/// 写入响应解析结果。
/// </summary>
internal readonly record struct WriteResponseInfo(byte ReturnCode);

/// <summary>
/// S7 响应解析。
/// </summary>
internal static class S7ResponseParser
{
    /// <summary>
    /// 解析并校验 S7 响应头。
    /// </summary>
    /// <param name="payload">S7 净荷（已剥离 TPKT 与 COTP 头）。</param>
    /// <param name="expectedPduReference">期望的 PDU Reference。</param>
    /// <param name="stage">失败阶段。</param>
    /// <returns>响应头信息。</returns>
    internal static S7AckHeader ParseHeader(ReadOnlySpan<byte> payload, ushort expectedPduReference, S7OperationStage stage)
    {
        if (payload.Length < ProtocolConstants.S7AckHeaderSize)
        {
            throw Error(S7ErrorCode.InvalidS7Header, stage, $"S7 响应长度 {payload.Length} 小于 {ProtocolConstants.S7AckHeaderSize} 字节。");
        }

        if (payload[0] != ProtocolConstants.S7ProtocolId)
        {
            throw Error(S7ErrorCode.InvalidS7Header, stage, $"S7 协议标识非法：0x{payload[0]:X2}，期望 0x32。");
        }

        if (payload[1] != ProtocolConstants.S7RosctrAckData)
        {
            throw Error(S7ErrorCode.InvalidS7Header, stage, $"S7 响应报文类型非法：0x{payload[1]:X2}，期望 0x03。");
        }

        ushort pduReference = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        if (pduReference != expectedPduReference)
        {
            throw Error(
                S7ErrorCode.InvalidS7Header,
                stage,
                $"PDU Reference 不匹配：期望 {expectedPduReference}，实际 {pduReference}。");
        }

        int parameterLength = BinaryPrimitives.ReadUInt16BigEndian(payload[6..]);
        int dataLength = BinaryPrimitives.ReadUInt16BigEndian(payload[8..]);
        int totalLength = ProtocolConstants.S7AckHeaderSize + parameterLength + dataLength;
        if (totalLength > payload.Length)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                stage,
                $"S7 响应长度不足：报文声明 {totalLength} 字节，实际 {payload.Length} 字节。");
        }

        return new S7AckHeader(pduReference, parameterLength, dataLength);
    }

    /// <summary>
    /// 解析通讯设置响应中的 PDU 大小。
    /// </summary>
    /// <param name="payload">S7 净荷。</param>
    /// <param name="expectedPduReference">期望的 PDU Reference。</param>
    /// <returns>PLC 确认的 PDU 大小；缺失时返回 0。</returns>
    internal static int ParseSetupResponse(ReadOnlySpan<byte> payload, ushort expectedPduReference)
    {
        S7AckHeader header = ParseHeader(payload, expectedPduReference, S7OperationStage.S7Setup);

        if (header.ParameterLength < 8 || payload.Length < 20)
        {
            return 0;
        }

        if (payload[12] != ProtocolConstants.S7FunctionSetup)
        {
            throw Error(
                S7ErrorCode.InvalidSetupResponse,
                S7OperationStage.S7Setup,
                $"通讯设置响应功能码非法：0x{payload[12]:X2}，期望 0xF0。");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(payload[18..]);
    }

    /// <summary>
    /// 解析读取响应。
    /// </summary>
    /// <param name="payload">S7 净荷。</param>
    /// <param name="expectedPduReference">期望的 PDU Reference。</param>
    /// <param name="expectedLength">本段期望字节数。</param>
    /// <returns>返回码、数据偏移与数据长度。</returns>
    internal static ReadResponseInfo ParseReadResponse(ReadOnlySpan<byte> payload, ushort expectedPduReference, int expectedLength)
    {
        S7AckHeader header = ParseHeader(payload, expectedPduReference, S7OperationStage.Read);

        if (header.ParameterLength < 2)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"读取响应参数长度不足：声明 {header.ParameterLength} 字节。");
        }

        int dataStart = ProtocolConstants.S7AckHeaderSize + header.ParameterLength;
        if (header.DataLength < 4 || dataStart + 4 > payload.Length)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"读取响应数据长度不足：声明 {header.DataLength} 字节，至少需要 4 字节结果头。");
        }

        byte returnCode = payload[dataStart];
        byte transportSize = payload[dataStart + 1];
        int length = BinaryPrimitives.ReadUInt16BigEndian(payload[(dataStart + 2)..]);
        int actualLength = transportSize == 0x03 ? (length + 7) / 8 : length / 8;
        int dataOffset = dataStart + 4;

        if (dataOffset + actualLength > payload.Length)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"读取响应数据被截断：声明 {actualLength} 字节，可用 {payload.Length - dataOffset} 字节。");
        }

        if (returnCode == ProtocolConstants.SuccessReturnCode && actualLength < expectedLength)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"读取响应数据长度不足：期望 {expectedLength} 字节，实际 {actualLength} 字节。");
        }

        return new ReadResponseInfo(returnCode, dataOffset, actualLength);
    }

    /// <summary>
    /// 解析写入响应。
    /// </summary>
    /// <param name="payload">S7 净荷。</param>
    /// <param name="expectedPduReference">期望的 PDU Reference。</param>
    /// <returns>返回码。</returns>
    internal static WriteResponseInfo ParseWriteResponse(ReadOnlySpan<byte> payload, ushort expectedPduReference)
    {
        S7AckHeader header = ParseHeader(payload, expectedPduReference, S7OperationStage.Write);

        int dataStart = ProtocolConstants.S7AckHeaderSize + header.ParameterLength;
        if (header.ParameterLength < 2 || header.DataLength < 1 || dataStart >= payload.Length)
        {
            throw Error(
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Write,
                $"写入响应长度不足：参数 {header.ParameterLength} 字节，数据 {header.DataLength} 字节。");
        }

        return new WriteResponseInfo(payload[dataStart]);
    }

    private static S7ClientException Error(S7ErrorCode code, S7OperationStage stage, string message)
    {
        return new S7ClientException(S7ErrorCategory.Protocol, code, stage, message);
    }
}