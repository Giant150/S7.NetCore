using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Typed;

public sealed class ProtocolBitWriteTests
{
    [Fact]
    public void Db1Bit3True_MatchesReferenceBytes()
    {
        byte[] frame = S7RequestBuilder.BuildWriteBitRequest(
            S7Area.DataBlock, dbNumber: 1, startByte: 0, bitNumber: 3, value: true, pduReference: 0);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x24,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x05,
            0x05, 0x01, 0x12, 0x0A, 0x10, 0x01, 0x00, 0x01, 0x00, 0x01, 0x84,
            0x00, 0x00, 0x03,
            0x00, 0x03, 0x00, 0x01, 0x01
        ];

        Assert.Equal(ProtocolConstants.WriteBitRequestFrameSize, frame.Length);
        Assert.Equal(expected, frame);
        Assert.Equal(ProtocolConstants.BitTransportSizeParameter, frame[22]);
        Assert.Equal(ProtocolConstants.BitTransportSizeData, frame[32]);
    }

    [Fact]
    public void Db2Bit8False_MatchesReferenceBytes()
    {
        byte[] frame = S7RequestBuilder.BuildWriteBitRequest(
            S7Area.DataBlock, dbNumber: 2, startByte: 8, bitNumber: 0, value: false, pduReference: 0);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x24,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x05,
            0x05, 0x01, 0x12, 0x0A, 0x10, 0x01, 0x00, 0x01, 0x00, 0x02, 0x84,
            0x00, 0x00, 0x40,
            0x00, 0x03, 0x00, 0x01, 0x00
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void MemoryBit3True_MatchesReferenceBytes()
    {
        byte[] frame = S7RequestBuilder.BuildWriteBitRequest(
            S7Area.Memory, dbNumber: 0, startByte: 0, bitNumber: 3, value: true, pduReference: 0);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x24,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x05,
            0x05, 0x01, 0x12, 0x0A, 0x10, 0x01, 0x00, 0x01, 0x00, 0x00, 0x83,
            0x00, 0x00, 0x03,
            0x00, 0x03, 0x00, 0x01, 0x01
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void OutputBit7True_MatchesReferenceBytes()
    {
        byte[] frame = S7RequestBuilder.BuildWriteBitRequest(
            S7Area.Output, dbNumber: 0, startByte: 2, bitNumber: 7, value: true, pduReference: 0);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x24,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x05,
            0x05, 0x01, 0x12, 0x0A, 0x10, 0x01, 0x00, 0x01, 0x00, 0x00, 0x82,
            0x00, 0x00, 0x17,
            0x00, 0x03, 0x00, 0x01, 0x01
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void ExistingGoldenFrames_AreUnchanged()
    {
        Assert.Equal(25, S7RequestBuilder.BuildSetupCommunication(960, 0xFFFF).Length);
        Assert.Equal(31, S7RequestBuilder.BuildReadRequest(S7Area.DataBlock, 1, 0, 8, 0xFFFF).Length);
        Assert.Equal(
            ProtocolConstants.WriteRequestOverhead + 3,
            S7RequestBuilder.BuildWriteRequest(S7Area.DataBlock, 1, 0, new byte[3], 0xFFFF).Length);
    }
}