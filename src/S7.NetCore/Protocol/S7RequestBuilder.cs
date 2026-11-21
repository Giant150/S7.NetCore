using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// S7 请求报文构造。
/// </summary>
internal static class S7RequestBuilder
{
    /// <summary>
    /// 构造 S7 通讯设置请求。
    /// </summary>
    /// <param name="requestedPduSize">申请的 PDU 大小。</param>
    /// <param name="pduReference">PDU Reference。</param>
    /// <returns>完整 TPKT 帧。</returns>
    internal static byte[] BuildSetupCommunication(int requestedPduSize, ushort pduReference)
    {
        var frame = new byte[ProtocolConstants.SetupCommunicationFrameSize];
        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.S7Setup);

        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;

        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrJob;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x0008);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0000);

        frame[17] = ProtocolConstants.S7FunctionSetup;
        frame[18] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(19), 0x0003);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(21), 0x0003);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), (ushort)requestedPduSize);

        return frame;
    }

    /// <summary>
    /// 构造单条字节读取请求。
    /// </summary>
    /// <param name="area">存储区域。</param>
    /// <param name="dbNumber">数据块号。</param>
    /// <param name="startByte">起始字节地址。</param>
    /// <param name="count">本段读取字节数。</param>
    /// <param name="pduReference">PDU Reference。</param>
    /// <returns>完整 TPKT 帧，长度 31 字节。</returns>
    internal static byte[] BuildReadRequest(S7Area area, int dbNumber, int startByte, int count, ushort pduReference)
    {
        var frame = new byte[ProtocolConstants.ReadRequestFrameSize];
        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.Read);

        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;

        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrJob;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x000E);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0000);

        frame[17] = ProtocolConstants.S7FunctionRead;
        frame[18] = 0x01;

        frame[19] = 0x12;
        frame[20] = 0x0A;
        frame[21] = 0x10;
        frame[22] = ProtocolConstants.TransportSizeByte;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(25), (ushort)dbNumber);
        frame[27] = (byte)area;
        WriteBitAddress(frame.AsSpan(28), startByte, bitNumber: 0);

        return frame;
    }

    /// <summary>
    /// 构造单条字节写入请求。
    /// </summary>
    /// <param name="area">存储区域。</param>
    /// <param name="dbNumber">数据块号。</param>
    /// <param name="startByte">起始字节地址。</param>
    /// <param name="data">本段写入数据。</param>
    /// <param name="pduReference">PDU Reference。</param>
    /// <returns>完整 TPKT 帧，长度 35 加数据长度。</returns>
    internal static byte[] BuildWriteRequest(
        S7Area area,
        int dbNumber,
        int startByte,
        ReadOnlySpan<byte> data,
        ushort pduReference)
    {
        int totalLength = ProtocolConstants.WriteRequestOverhead + data.Length;
        var frame = new byte[totalLength];
        TpktCodec.WriteHeader(frame, totalLength, S7OperationStage.Write);

        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;

        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrJob;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x000E);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), (ushort)(data.Length + 4));

        frame[17] = ProtocolConstants.S7FunctionWrite;
        frame[18] = 0x01;

        frame[19] = 0x12;
        frame[20] = 0x0A;
        frame[21] = 0x10;
        frame[22] = ProtocolConstants.TransportSizeByte;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), (ushort)data.Length);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(25), (ushort)dbNumber);
        frame[27] = (byte)area;
        WriteBitAddress(frame.AsSpan(28), startByte, bitNumber: 0);

        frame[31] = 0x00;
        frame[32] = ProtocolConstants.TransportSizeOctet;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(33), (ushort)(data.Length * 8));

        data.CopyTo(frame.AsSpan(ProtocolConstants.WriteRequestOverhead));
        return frame;
    }

    /// <summary>
    /// 构造单请求位写（36 字节）。参数区传输尺寸为 Bit(0x01)，数据区传输尺寸为 Bit(0x03)。
    /// </summary>
    internal static byte[] BuildWriteBitRequest(
        S7Area area,
        int dbNumber,
        int startByte,
        int bitNumber,
        bool value,
        ushort pduReference)
    {
        var frame = new byte[ProtocolConstants.WriteBitRequestFrameSize];
        TpktCodec.WriteHeader(frame, frame.Length, S7OperationStage.Write);

        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;

        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrJob;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), 0x000E);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), 0x0005);

        frame[17] = ProtocolConstants.S7FunctionWrite;
        frame[18] = 0x01;

        frame[19] = 0x12;
        frame[20] = 0x0A;
        frame[21] = 0x10;
        frame[22] = ProtocolConstants.BitTransportSizeParameter;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(23), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(25), (ushort)dbNumber);
        frame[27] = (byte)area;
        WriteBitAddress(frame.AsSpan(28), startByte, bitNumber);

        frame[31] = 0x00;
        frame[32] = ProtocolConstants.BitTransportSizeData;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(33), 0x0001);
        frame[35] = value ? (byte)1 : (byte)0;

        return frame;
    }

    private static void WriteBitAddress(Span<byte> destination, int startByte, int bitNumber)
    {
        int bitAddress = startByte * 8 + bitNumber;
        destination[0] = (byte)(bitAddress >> 16);
        destination[1] = (byte)(bitAddress >> 8);
        destination[2] = (byte)bitAddress;
    }
}