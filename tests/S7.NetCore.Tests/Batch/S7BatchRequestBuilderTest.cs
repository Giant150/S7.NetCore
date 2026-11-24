using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>多条目读/写请求帧构造。</summary>
public sealed class S7BatchRequestBuilderTest
{
    [Fact]
    public void BuildReadRequest_EmitsSingleFrameWithOneHeaderPerItem()
    {
        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[4]),
            new BatchWireItem(S7Area.Input, 0, 8, new byte[2])
        ];

        byte[] frame = S7BatchRequestBuilder.BuildReadRequest(items, 0x1234);

        Assert.Equal(ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobPayloadHeaderSize
            + (ProtocolConstants.ReadItemHeaderSize * items.Length), frame.Length);
        Assert.Equal(0x03, frame[0]);
        Assert.Equal(0x02, frame[4]);
        Assert.Equal(ProtocolConstants.CotpPduTypeData, frame[5]);
        Assert.Equal(0x80, frame[6]);
        Assert.Equal(ProtocolConstants.S7ProtocolId, frame[7]);
        Assert.Equal(ProtocolConstants.S7RosctrJob, frame[8]);
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(11)));
        Assert.Equal(2 + (12 * items.Length), BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(13)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(15)));
        Assert.Equal(ProtocolConstants.S7FunctionRead, frame[17]);
        Assert.Equal(items.Length, frame[18]);

        int offset = 19;
        AssertItemHeader(frame.AsSpan(offset, 12), S7Area.DataBlock, 1, 0, 4, isBit: false, bitNumber: 0);
        offset += 12;
        AssertItemHeader(frame.AsSpan(offset, 12), S7Area.Input, 0, 8, 2, isBit: false, bitNumber: 0);
    }

    [Fact]
    public void BuildWriteRequest_MatchesCapacityCalculatorAndAlignment()
    {
        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[] { 0x11, 0x22, 0x33 }),
            new BatchWireItem(S7Area.Memory, 0, 10, new byte[] { 0x44, 0x55 })
        ];

        byte[] frame = S7BatchRequestBuilder.BuildWriteRequest(items, 7);

        int expectedLength = ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobPayloadHeaderSize
            + (ProtocolConstants.ReadItemHeaderSize * items.Length)
            + (ProtocolConstants.WriteItemDataHeaderSize + 3 + 1)
            + (ProtocolConstants.WriteItemDataHeaderSize + 2);

        Assert.Equal(expectedLength, frame.Length);
        Assert.Equal(
            expectedLength - ProtocolConstants.TpktHeaderSize - ProtocolConstants.CotpDataHeaderSize,
            BatchCapacityCalculator.WriteRequestRequired(items));
        Assert.Equal(ProtocolConstants.S7FunctionWrite, frame[17]);
        Assert.Equal(items.Length, frame[18]);

        int parameterOffset = 19;
        int dataOffset = 19 + (12 * items.Length);
        AssertItemHeader(frame.AsSpan(parameterOffset, 12), S7Area.DataBlock, 1, 0, 3, isBit: false, bitNumber: 0);

        Assert.Equal(0x00, frame[dataOffset]);
        Assert.Equal(ProtocolConstants.TransportSizeOctet, frame[dataOffset + 1]);
        Assert.Equal(24, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(dataOffset + 2)));
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, frame.AsSpan(dataOffset + 4, 3).ToArray());
        Assert.Equal(0x00, frame[dataOffset + 7]);

        dataOffset += 4 + 3 + 1;
        Assert.Equal(0x00, frame[dataOffset]);
        Assert.Equal(ProtocolConstants.TransportSizeOctet, frame[dataOffset + 1]);
        Assert.Equal(16, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(dataOffset + 2)));
        Assert.Equal(new byte[] { 0x44, 0x55 }, frame.AsSpan(dataOffset + 4, 2).ToArray());
    }

    [Fact]
    public void BuildWriteRequest_BitItemUsesBitTransportSizeAndBitAddress()
    {
        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 2, 3, new byte[] { 1 }, IsBit: true, BitNumber: 5)
        ];

        byte[] frame = S7BatchRequestBuilder.BuildWriteRequest(items, 9);

        int parameterOffset = 19;
        Span<byte> header = frame.AsSpan(parameterOffset, 12);
        Assert.Equal(0x12, header[0]);
        Assert.Equal(0x0A, header[1]);
        Assert.Equal(0x10, header[2]);
        Assert.Equal(ProtocolConstants.BitTransportSizeParameter, header[3]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(header[4..]));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(header[6..]));
        Assert.Equal((byte)S7Area.DataBlock, header[8]);
        Assert.Equal((3 * 8) + 5, (header[9] << 16) | (header[10] << 8) | header[11]);

        int dataOffset = 19 + 12;
        Assert.Equal(0x00, frame[dataOffset]);
        Assert.Equal(ProtocolConstants.BitTransportSizeData, frame[dataOffset + 1]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(dataOffset + 2)));
        Assert.Equal(1, frame[dataOffset + 4]);
    }

    private static void AssertItemHeader(
        ReadOnlySpan<byte> header,
        S7Area area,
        int dbNumber,
        int startByte,
        int length,
        bool isBit,
        int bitNumber)
    {
        Assert.Equal(0x12, header[0]);
        Assert.Equal(0x0A, header[1]);
        Assert.Equal(0x10, header[2]);
        Assert.Equal(isBit ? ProtocolConstants.BitTransportSizeParameter : ProtocolConstants.TransportSizeByte, header[3]);
        Assert.Equal(length, BinaryPrimitives.ReadUInt16BigEndian(header[4..]));
        Assert.Equal(dbNumber, BinaryPrimitives.ReadUInt16BigEndian(header[6..]));
        Assert.Equal((byte)area, header[8]);
        int expectedBitAddress = (startByte * 8) + (isBit ? bitNumber : 0);
        Assert.Equal(expectedBitAddress, (header[9] << 16) | (header[10] << 8) | header[11]);
    }
}