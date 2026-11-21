using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Typed;

public sealed class DataConversionTests
{
    [Fact]
    public void T_C01_OccupiedBytes_MatchTypeRules()
    {
        Assert.Equal(1, S7TypeRules.GetOccupiedBytes(S7DataType.Bool, 0));
        Assert.Equal(1, S7TypeRules.GetOccupiedBytes(S7DataType.Byte, 0));
        Assert.Equal(2, S7TypeRules.GetOccupiedBytes(S7DataType.Word, 0));
        Assert.Equal(2, S7TypeRules.GetOccupiedBytes(S7DataType.Int, 0));
        Assert.Equal(4, S7TypeRules.GetOccupiedBytes(S7DataType.DWord, 0));
        Assert.Equal(4, S7TypeRules.GetOccupiedBytes(S7DataType.DInt, 0));
        Assert.Equal(4, S7TypeRules.GetOccupiedBytes(S7DataType.Real, 0));
        Assert.Equal(8, S7TypeRules.GetOccupiedBytes(S7DataType.LReal, 0));
        Assert.Equal(16, S7TypeRules.GetOccupiedBytes(S7DataType.String, 16));
        Assert.Equal(18, S7TypeRules.GetOccupiedBytes(S7DataType.S7String, 16));
        Assert.Equal(36, S7TypeRules.GetOccupiedBytes(S7DataType.S7WString, 16));
        Assert.Equal(504, S7TypeRules.GetOccupiedBytes(S7DataType.S7WString, 250));
        Assert.Equal(32770, S7TypeRules.GetOccupiedBytes(S7DataType.S7WString, 16383));
    }

    [Fact]
    public void T_C02_WordAndInt_UseBigEndianTwosComplement()
    {
        byte[] word = S7ValueCodec.Encode(S7DataType.Word, S7Value.FromUInt16(0x1234), 0);
        Assert.Equal(new byte[] { 0x12, 0x34 }, word);

        byte[] intBytes = S7ValueCodec.Encode(S7DataType.Int, S7Value.FromInt16(-12345), 0);
        Assert.Equal(new byte[] { 0xCF, 0xC7 }, intBytes);

        Assert.Equal((short)-12345, S7ValueCodec.Decode(S7DataType.Int, intBytes, 0).AsInt16());
        Assert.Equal((ushort)53191, S7ValueCodec.Decode(S7DataType.Word, intBytes, 0).AsUInt16());
    }

    [Fact]
    public void T_C03_RealAndLReal_PreserveBitPatterns()
    {
        float[] singles =
        [
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
            0.0f,
            -0.0f,
            3.14159f
        ];

        foreach (float value in singles)
        {
            byte[] encoded = S7ValueCodec.Encode(S7DataType.Real, S7Value.FromSingle(value), 0);
            float decoded = S7ValueCodec.Decode(S7DataType.Real, encoded, 0).AsSingle();
            Assert.Equal(BitConverter.SingleToInt32Bits(value), BitConverter.SingleToInt32Bits(decoded));
        }

        double[] doubles =
        [
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            0.0,
            -0.0,
            2.718281828459045
        ];

        foreach (double value in doubles)
        {
            byte[] encoded = S7ValueCodec.Encode(S7DataType.LReal, S7Value.FromDouble(value), 0);
            double decoded = S7ValueCodec.Decode(S7DataType.LReal, encoded, 0).AsDouble();
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(decoded));
        }
    }

    [Fact]
    public void T_C04_EncodingIsBigEndian_IndependentOfHost()
    {
        Assert.Equal(
            new byte[] { 0x11, 0x22, 0x33, 0x44 },
            S7ValueCodec.Encode(S7DataType.DWord, S7Value.FromUInt32(0x11223344), 0));

        Assert.Equal(
            new byte[] { 0x01, 0x02 },
            S7ValueCodec.Encode(S7DataType.Word, S7Value.FromUInt16(0x0102), 0));

        Assert.Equal(
            8,
            S7ValueCodec.Encode(S7DataType.LReal, S7Value.FromDouble(0), 0).Length);
    }

    [Fact]
    public void T_C05_S7String_EncodesHeaderAndPadsZero()
    {
        byte[] encoded = S7ValueCodec.Encode(S7DataType.S7String, S7Value.FromString("S7-PLC"), 16);

        Assert.Equal(18, encoded.Length);
        Assert.Equal(16, encoded[0]);
        Assert.Equal(6, encoded[1]);
        Assert.Equal("S7-PLC"u8.ToArray(), encoded[2..8]);
        Assert.All(encoded[8..], value => Assert.Equal(0, value));
        Assert.Equal("S7-PLC", S7ValueCodec.Decode(S7DataType.S7String, encoded, 16).AsString());
    }

    [Fact]
    public void T_C06_CString_EncodesTerminatorAndPadsZero()
    {
        byte[] encoded = S7ValueCodec.Encode(S7DataType.String, S7Value.FromString("HELLO"), 8);

        Assert.Equal(8, encoded.Length);
        Assert.Equal("HELLO"u8.ToArray(), encoded[..5]);
        Assert.Equal(0, encoded[5]);
        Assert.Equal(0, encoded[6]);
        Assert.Equal(0, encoded[7]);
        Assert.Equal("HELLO", S7ValueCodec.Decode(S7DataType.String, encoded, 8).AsString());
    }

    [Fact]
    public void T_C07_NonAsciiWrite_ThrowsStringEncodingInvalid()
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Encode(S7DataType.String, S7Value.FromString("PLČ"), 8));

        Assert.Equal(S7ErrorCode.StringEncodingInvalid, exception.ErrorCode);
    }

    [Fact]
    public void T_C08_CapacityViolationAndInvalidHeader()
    {
        S7ClientException capacity = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Encode(S7DataType.String, S7Value.FromString("ABCDE"), 4));
        Assert.Equal(S7ErrorCode.StringCapacityInvalid, capacity.ErrorCode);

        var invalidHeader = new byte[18];
        invalidHeader[0] = 16;
        invalidHeader[1] = 17;

        S7ClientException header = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Decode(S7DataType.S7String, invalidHeader, 16));
        Assert.Equal(S7ErrorCode.InvalidS7StringHeader, header.ErrorCode);
        Assert.Equal(S7ErrorCategory.Protocol, header.Category);
    }

    [Fact]
    public void T_C09_InsufficientBytes_ThrowResponseTooShort()
    {
        Assert.Equal(
            S7ErrorCode.ResponseTooShort,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.Word, new byte[] { 0x12 }, 0)).ErrorCode);

        Assert.Equal(
            S7ErrorCode.ResponseTooShort,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.Real, new byte[] { 0x01, 0x02, 0x03 }, 0)).ErrorCode);

        Assert.Equal(
            S7ErrorCode.ResponseTooShort,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.String, new byte[7], 8)).ErrorCode);

        Assert.Equal(
            S7ErrorCode.InvalidS7StringHeader,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.S7String, new byte[] { 0x10 }, 16)).ErrorCode);
    }

    [Fact]
    public void T_C10_EncodeThenDecode_RoundTrips()
    {
        foreach (short value in new short[] { short.MinValue, -1, 0, 12345, short.MaxValue })
        {
            byte[] data = S7ValueCodec.Encode(S7DataType.Int, S7Value.FromInt16(value), 0);
            Assert.Equal(value, S7ValueCodec.Decode(S7DataType.Int, data, 0).AsInt16());
        }

        byte[] real = S7ValueCodec.Encode(S7DataType.Real, S7Value.FromSingle(3.14159f), 0);
        Assert.Equal(
            BitConverter.SingleToInt32Bits(3.14159f),
            BitConverter.SingleToInt32Bits(S7ValueCodec.Decode(S7DataType.Real, real, 0).AsSingle()));

        byte[] text = S7ValueCodec.Encode(S7DataType.S7String, S7Value.FromString("S7-PLC"), 16);
        Assert.Equal("S7-PLC", S7ValueCodec.Decode(S7DataType.S7String, text, 16).AsString());
    }

    [Fact]
    public void T_C11_RealEncoding_AllocatesAtMost256Bytes()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] encoded = S7ValueCodec.Encode(S7DataType.Real, S7Value.FromSingle(1.5f), 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(4, encoded.Length);
        Assert.InRange(allocated, 0, 256);
    }

    [Fact]
    public void T_CW01_EncodeWString_WritesHeaderAndUtf16Be()
    {
        byte[] encoded = S7ValueCodec.Encode(
            S7DataType.S7WString,
            S7Value.FromString("温度A1"),
            16);

        Assert.Equal(36, encoded.Length);
        Assert.Equal(new byte[] { 0x00, 0x10, 0x00, 0x04 }, encoded[..4]);
        Assert.Equal(
            new byte[] { 0x6E, 0x29, 0x5E, 0xA6, 0x00, 0x41, 0x00, 0x31 },
            encoded[4..12]);
        Assert.All(encoded[12..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void T_CW02_EncodeWString_EmptyString()
    {
        byte[] encoded = S7ValueCodec.Encode(
            S7DataType.S7WString,
            S7Value.FromString(string.Empty),
            16);

        Assert.Equal(36, encoded.Length);
        Assert.Equal(new byte[] { 0x00, 0x10, 0x00, 0x00 }, encoded[..4]);
        Assert.All(encoded[4..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void T_CW03_EncodeWString_SurrogatePair()
    {
        byte[] encoded = S7ValueCodec.Encode(
            S7DataType.S7WString,
            S7Value.FromString("温度😀"),
            8);

        Assert.Equal(20, encoded.Length);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x00, 0x04 }, encoded[..4]);
        Assert.Equal(new byte[] { 0x6E, 0x29, 0x5E, 0xA6 }, encoded[4..8]);
        Assert.Equal(new byte[] { 0xD8, 0x3D, 0xDE, 0x00 }, encoded[8..12]);
        Assert.All(encoded[12..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void T_CW04_EncodeWString_Failures()
    {
        S7ClientException capacity = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Encode(
                S7DataType.S7WString,
                S7Value.FromString("ABCDE"),
                4));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, capacity.ErrorCode);
        Assert.Contains("5", capacity.Message, StringComparison.Ordinal);
        Assert.Contains("4", capacity.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDE", capacity.Message, StringComparison.Ordinal);

        S7ClientException surrogate = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Encode(
                S7DataType.S7WString,
                S7Value.FromString("\uD83D"),
                16));

        Assert.Equal(S7ErrorCode.StringEncodingInvalid, surrogate.ErrorCode);
        Assert.Contains("0", surrogate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void T_CW05_DecodeWString_HeaderLengthGreaterThanHeaderCapacity_Throws408()
    {
        byte[] data = new byte[36];
        data[0] = 0x00;
        data[1] = 0x10;
        data[2] = 0x00;
        data[3] = 0x11;

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Decode(S7DataType.S7WString, data, 16));

        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
        Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
    }

    [Fact]
    public void T_CW06_DecodeWString_HeaderCapacityGreaterThanRequest_Throws408()
    {
        byte[] data = new byte[36];
        data[0] = 0x00;
        data[1] = 0x14;
        data[2] = 0x00;
        data[3] = 0x04;

        Assert.Equal(
            S7ErrorCode.InvalidS7WStringHeader,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.S7WString, data, 16)).ErrorCode);

        var largerRequest = new byte[204];
        largerRequest[0] = 0x00;
        largerRequest[1] = 0x10;
        largerRequest[2] = 0x00;
        largerRequest[3] = 0x04;
        WriteUtf16Be(largerRequest, 4, "温度A1");

        string decoded = S7ValueCodec.Decode(
            S7DataType.S7WString,
            largerRequest,
            100).AsString();

        Assert.Equal("温度A1", decoded);
    }

    [Fact]
    public void T_CW07_DecodeWString_InvalidSurrogates_Throw409()
    {
        byte[] lonelyLow = new byte[36];
        lonelyLow[0] = 0x00;
        lonelyLow[1] = 0x10;
        lonelyLow[2] = 0x00;
        lonelyLow[3] = 0x01;
        lonelyLow[4] = 0xDE;
        lonelyLow[5] = 0x00;

        S7ClientException low = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Decode(S7DataType.S7WString, lonelyLow, 16));

        Assert.Equal(S7ErrorCode.InvalidS7WStringData, low.ErrorCode);
        Assert.Equal(S7OperationStage.Read, low.Stage);
        Assert.Contains("0", low.Message, StringComparison.Ordinal);

        byte[] incompleteHigh = new byte[36];
        incompleteHigh[0] = 0x00;
        incompleteHigh[1] = 0x10;
        incompleteHigh[2] = 0x00;
        incompleteHigh[3] = 0x01;
        incompleteHigh[4] = 0xD8;
        incompleteHigh[5] = 0x3D;

        Assert.Equal(
            S7ErrorCode.InvalidS7WStringData,
            Assert.Throws<S7ClientException>(
                () => S7ValueCodec.Decode(S7DataType.S7WString, incompleteHigh, 16)).ErrorCode);
    }

    [Fact]
    public void T_CW08_DecodeWString_ShortData_Throws404()
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7ValueCodec.Decode(S7DataType.S7WString, new byte[35], 16));

        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
        Assert.Equal(S7ErrorCode.ResponseTooShort, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
    }

    private static void WriteUtf16Be(byte[] destination, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            destination[offset + (i * 2)] = (byte)(text[i] >> 8);
            destination[offset + (i * 2) + 1] = (byte)text[i];
        }
    }
}