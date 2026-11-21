using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests;

public sealed class ProtocolGoldenTests
{
    [Fact]
    public void CotpConnectionRequest_MatchesReferenceBytes()
    {
        byte[] frame = CotpCodec.BuildConnectionRequest(
            new Tsap(0x01, 0x00),
            new Tsap(0x03, 0x01),
            S7OperationStage.CotpConnect);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x16,
            0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC1, 0x02, 0x01, 0x00,
            0xC2, 0x02, 0x03, 0x01,
            0xC0, 0x01, 0x0A
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void SetupCommunication_960_MatchesReferenceBytes()
    {
        byte[] frame = S7RequestBuilder.BuildSetupCommunication(960, 0xFFFF);

        byte[] expected =
        [
            3, 0, 0, 25,
            2, 240, 128,
            50, 1, 0, 0, 255, 255, 0, 8, 0, 0,
            240, 0, 0, 3, 0, 3, 3, 192
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void SetupCommunication_480_UsesRequestedPdu()
    {
        byte[] frame = S7RequestBuilder.BuildSetupCommunication(480, 0x0001);

        Assert.Equal(25, frame.Length);
        Assert.Equal(0x00, frame[11]);
        Assert.Equal(0x01, frame[12]);
        Assert.Equal(0x01, frame[23]);
        Assert.Equal(0xE0, frame[24]);
    }

    [Fact]
    public void ReadRequest_DataBlock_MatchesReferenceLayout()
    {
        byte[] frame = S7RequestBuilder.BuildReadRequest(
            S7Area.DataBlock,
            dbNumber: 1,
            startByte: 0,
            count: 8,
            pduReference: 0xFFFF);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x1F,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x0E, 0x00, 0x00,
            0x04, 0x01,
            0x12, 0x0A, 0x10, 0x02,
            0x00, 0x08,
            0x00, 0x01,
            0x84,
            0x00,
            0x00, 0x00
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void ReadRequest_AddressAbove8191_UsesOverflowByte()
    {
        byte[] frame = S7RequestBuilder.BuildReadRequest(
            S7Area.DataBlock,
            dbNumber: 1,
            startByte: 8192,
            count: 1,
            pduReference: 1);

        Assert.Equal(0x01, frame[28]);
        Assert.Equal(0x00, frame[29]);
        Assert.Equal(0x00, frame[30]);
    }

    [Fact]
    public void WriteRequest_MatchesReferenceLayout_ExceptPduReference()
    {
        byte[] frame = S7RequestBuilder.BuildWriteRequest(
            S7Area.DataBlock,
            dbNumber: 1,
            startByte: 0,
            data: [0xAA, 0xBB, 0xCC],
            pduReference: 0xFFFF);

        byte[] expected =
        [
            0x03, 0x00, 0x00, 0x26,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x0E, 0x00, 0x07,
            0x05, 0x01,
            0x12, 0x0A, 0x10, 0x02,
            0x00, 0x03,
            0x00, 0x01,
            0x84,
            0x00,
            0x00, 0x00,
            0x00, 0x04, 0x00, 0x18,
            0xAA, 0xBB, 0xCC
        ];

        Assert.Equal(expected, frame);
    }

    [Fact]
    public void WriteRequest_NonDataBlock_UsesZeroDbNumber()
    {
        byte[] frame = S7RequestBuilder.BuildWriteRequest(
            S7Area.Memory,
            dbNumber: 0,
            startByte: 16,
            data: [0x01],
            pduReference: 2);

        Assert.Equal(0x00, frame[25]);
        Assert.Equal(0x00, frame[26]);
        Assert.Equal(0x83, frame[27]);
        Assert.Equal(0x00, frame[28]);
        Assert.Equal(0x00, frame[29]);
        Assert.Equal(0x80, frame[30]);
    }
}