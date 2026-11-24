using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests;

public sealed class ProtocolParsingTests
{
    [Fact]
    public void ParseReadResponse_Success_ReturnsData()
    {
        byte[] payload = BuildReadPayload(reference: 0x1234, returnCode: 0xFF, actualCount: 8);

        ReadResponseInfo info = S7ResponseParser.ParseReadResponse(payload, 0x1234, expectedLength: 8);

        Assert.Equal(0xFF, info.ReturnCode);
        Assert.Equal(8, info.DataLength);
        Assert.Equal(18, info.DataOffset);
        Assert.Equal(payload.AsSpan(18, 8).ToArray(), payload.AsSpan(info.DataOffset, info.DataLength).ToArray());
    }

    [Fact]
    public void ParseReadResponse_PlcError_PreservesReturnCode()
    {
        byte[] payload = BuildReadPayload(reference: 7, returnCode: 0x05, actualCount: 0);

        ReadResponseInfo info = S7ResponseParser.ParseReadResponse(payload, 7, expectedLength: 8);

        Assert.Equal(0x05, info.ReturnCode);
    }

    [Fact]
    public void ParseReadResponse_DataTooShort_ThrowsProtocolError()
    {
        byte[] payload = BuildReadPayload(reference: 7, returnCode: 0xFF, actualCount: 3);

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ResponseParser.ParseReadResponse(payload, 7, expectedLength: 8));

        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
        Assert.Equal(S7ErrorCode.ResponseTooShort, exception.ErrorCode);
    }

    [Fact]
    public void ParseReadResponse_PduReferenceMismatch_ThrowsProtocolError()
    {
        byte[] payload = BuildReadPayload(reference: 8, returnCode: 0xFF, actualCount: 1);

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ResponseParser.ParseReadResponse(payload, 9, expectedLength: 1));

        Assert.Equal(S7ErrorCode.InvalidS7Header, exception.ErrorCode);
    }

    [Fact]
    public void ParseReadResponse_InvalidProtocolId_ThrowsProtocolError()
    {
        byte[] payload = BuildReadPayload(reference: 1, returnCode: 0xFF, actualCount: 1);
        payload[0] = 0x33;

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ResponseParser.ParseReadResponse(payload, 1, expectedLength: 1));

        Assert.Equal(S7ErrorCode.InvalidS7Header, exception.ErrorCode);
    }

    [Fact]
    public void ParseSetupResponse_ReturnsConfirmedPdu()
    {
        byte[] payload = BuildSetupPayload(reference: 0x0102, pduSize: 480);

        int confirmed = S7ResponseParser.ParseSetupResponse(payload, 0x0102);

        Assert.Equal(480, confirmed);
    }

    [Fact]
    public void ParseSetupResponse_MissingPdu_ReturnsZero()
    {
        byte[] payload = BuildSetupPayload(reference: 1, pduSize: 0);

        int confirmed = S7ResponseParser.ParseSetupResponse(payload, 1);

        Assert.Equal(0, confirmed);
    }

    [Theory]
    [InlineData(480, 0, 240)]
    [InlineData(480, 200, 240)]
    [InlineData(480, 480, 480)]
    [InlineData(480, 960, 480)]
    [InlineData(960, 960, 960)]
    [InlineData(240, 480, 240)]
    public void PduNegotiation_ClampsToRequestedRange(int requested, int confirmed, int expectedEffective)
    {
        PduNegotiation negotiation = PduSizeNegotiator.Negotiate(requested, confirmed);

        Assert.Equal(expectedEffective, negotiation.Effective);
        Assert.Equal(expectedEffective - 18, negotiation.ReadPayloadLimit);
        Assert.Equal(expectedEffective - ProtocolConstants.WriteRequestPayloadOverhead, negotiation.WritePayloadLimit);
    }

    [Theory]
    [InlineData(0x01, S7ErrorCode.PlcHardwareFault)]
    [InlineData(0x03, S7ErrorCode.PlcAccessDenied)]
    [InlineData(0x05, S7ErrorCode.PlcAddressOutOfRange)]
    [InlineData(0x06, S7ErrorCode.PlcDataTypeNotSupported)]
    [InlineData(0x07, S7ErrorCode.PlcDataInconsistent)]
    [InlineData(0x0A, S7ErrorCode.PlcObjectNotExist)]
    [InlineData(0x42, S7ErrorCode.PlcUnknownReturnCode)]
    public void PlcReturnCodeMapper_MapsKnownAndUnknown(byte returnCode, S7ErrorCode expectedCode)
    {
        (S7ErrorCode code, string message) = PlcReturnCodeMapper.Map(returnCode);

        Assert.Equal(expectedCode, code);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void TsapResolver_ExplicitValuesOverrideDefaults()
    {
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            LocalTsap = new Tsap(0x11, 0x22),
            RemoteTsap = new Tsap(0x33, 0x44)
        };

        (Tsap local, Tsap remote) = TsapResolver.Resolve(options);

        Assert.Equal(new Tsap(0x11, 0x22), local);
        Assert.Equal(new Tsap(0x33, 0x44), remote);
    }

    [Theory]
    [InlineData(S7CpuType.S71200, 0, 1, 0x01, 0x00, 0x03, 0x01)]
    [InlineData(S7CpuType.S7300, 0, 2, 0x01, 0x00, 0x03, 0x02)]
    [InlineData(S7CpuType.S7200, 0, 0, 0x10, 0x00, 0x10, 0x01)]
    [InlineData(S7CpuType.Logo0BA8, 0, 0, 0x01, 0x00, 0x01, 0x02)]
    public void TsapResolver_DerivesDefaultPair(
        S7CpuType cpuType,
        int rack,
        int slot,
        byte localFirst,
        byte localSecond,
        byte remoteFirst,
        byte remoteSecond)
    {
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = cpuType,
            Rack = rack,
            Slot = slot
        };

        (Tsap local, Tsap remote) = TsapResolver.Resolve(options);

        Assert.Equal(new Tsap(localFirst, localSecond), local);
        Assert.Equal(new Tsap(remoteFirst, remoteSecond), remote);
    }

    private static byte[] BuildReadPayload(ushort reference, byte returnCode, int actualCount)
    {
        int payloadLength = 4 + actualCount;
        var payload = new byte[12 + 2 + payloadLength];

        payload[0] = 0x32;
        payload[1] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), reference);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 0x0002);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)payloadLength);
        payload[12] = 0x04;
        payload[13] = 0x01;
        payload[14] = returnCode;
        payload[15] = 0x04;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16), (ushort)(actualCount * 8));

        for (int i = 0; i < actualCount; i++)
        {
            payload[18 + i] = (byte)(i + 1);
        }

        return payload;
    }

    private static byte[] BuildSetupPayload(ushort reference, int pduSize)
    {
        var payload = new byte[20];

        payload[0] = 0x32;
        payload[1] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), reference);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 0x0008);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), 0x0000);
        payload[12] = 0xF0;
        payload[13] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(14), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(16), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18), (ushort)pduSize);

        return payload;
    }
}