using System.Runtime.InteropServices;
using S7.NetCore;
using S7.NetCore.Internal;
using S7.NetCore.Tests;

namespace S7.NetCore.Tests.Struct;

public sealed class StructTests
{
    [Fact]
    public void ContractValuesAndAttribute_AreStable()
    {
        Assert.Equal(139, (int)S7ErrorCode.StructLayoutInvalid);
        Assert.Equal(140, (int)S7ErrorCode.StructAddressNotSupported);
        Assert.Equal(132, (int)S7ErrorCode.DataTypeNotSupported);
        Assert.Equal(136, (int)S7ErrorCode.StringCapacityInvalid);
        Assert.Equal(11, Enum.GetValues<S7DataType>().Length);

        AttributeUsageAttribute usage = typeof(S7StringFieldAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Field));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
        Assert.True(usage.AllowMultiple);

        var entry = new S7LogEntry { Operation = "ReadStruct" };
        Assert.Null(entry.StructTypeName);
        Assert.Null(entry.MappedFieldCount);
    }

    [Fact]
    public void Layout_NumericAndPadding_AreCalculatedByDeclarationOrder()
    {
        StructLayoutSnapshot numeric = StructLayoutResolver.Resolve<NumericStruct>();
        Assert.Equal(26, numeric.TotalLength);
        Assert.Equal(8, numeric.FieldCount);
        Assert.Equal(0, numeric.Fields[0].Offset);
        Assert.Equal(1, numeric.Fields[1].Offset);
        Assert.Equal(2, numeric.Fields[2].Offset);
        Assert.Equal(4, numeric.Fields[3].Offset);
        Assert.Equal(6, numeric.Fields[4].Offset);
        Assert.Equal(10, numeric.Fields[5].Offset);
        Assert.Equal(14, numeric.Fields[6].Offset);
        Assert.Equal(18, numeric.Fields[7].Offset);

        Assert.Equal(11, StructLayoutResolver.Resolve<NoPaddingStruct>().TotalLength);
        Assert.Equal(9, StructLayoutResolver.Resolve<PackStruct>().TotalLength);
    }

    [Fact]
    public void Layout_StringCapacitiesAndEncodings_AreMapped()
    {
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<StringStruct>();
        Assert.Equal(46, snapshot.TotalLength);
        Assert.Equal(18, snapshot.Fields[0].Length);
        Assert.Equal(20, snapshot.Fields[1].Length);
        Assert.Equal(8, snapshot.Fields[2].Length);
        Assert.Equal(S7DataType.S7String, snapshot.Fields[0].DataType);
        Assert.Equal(S7DataType.S7WString, snapshot.Fields[1].DataType);
        Assert.Equal(S7DataType.String, snapshot.Fields[2].DataType);
    }

    [Fact]
    public void Layout_InvalidShapes_ReturnStableErrors()
    {
        AssertLayoutError<MissingCapacityStruct>(S7ErrorCode.StringCapacityInvalid);
        AssertLayoutError<DuplicateCapacityStruct>(S7ErrorCode.StructLayoutInvalid);
        AssertLayoutError<PrivateAnnotatedStruct>(S7ErrorCode.StructLayoutInvalid);
        AssertLayoutError<PropertyAnnotatedStruct>(S7ErrorCode.StructLayoutInvalid);
        AssertLayoutError<ExplicitStruct>(S7ErrorCode.StructLayoutInvalid);
        AssertLayoutError<EmptyStruct>(S7ErrorCode.StructLayoutInvalid);
        AssertLayoutError<ArrayFieldStruct>(S7ErrorCode.DataTypeNotSupported);
        AssertLayoutError<NestedFieldStruct>(S7ErrorCode.DataTypeNotSupported);
        AssertLayoutError<DateTimeFieldStruct>(S7ErrorCode.DataTypeNotSupported);
        AssertLayoutError<DecimalFieldStruct>(S7ErrorCode.DataTypeNotSupported);
        AssertLayoutError<EnumFieldStruct>(S7ErrorCode.DataTypeNotSupported);
    }

    [Fact]
    public void Layout_PrivateAndStaticMembers_DoNotParticipate()
    {
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<MixedVisibilityStruct>();
        Assert.Equal(4, snapshot.TotalLength);
        Assert.Single(snapshot.Fields);
        Assert.Equal(nameof(MixedVisibilityStruct.PublicValue), snapshot.Fields[0].Name);
    }

    [Fact]
    public void Codec_NumericRoundTrip_PreservesAllFields()
    {
        var value = new NumericStruct
        {
            Flag = true,
            B = 7,
            S = -2,
            US = 40000,
            I = -123456,
            UI = 4_000_000_000,
            F = 3.25f,
            D = 2.718281828459045
        };

        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<NumericStruct>();
        byte[] encoded = StructEncoder.Encode(value, snapshot);
        Assert.Equal(26, encoded.Length);

        NumericStruct decoded = StructDecoder.Decode<NumericStruct>(encoded, snapshot);
        Assert.True(decoded.Flag);
        Assert.Equal(7, decoded.B);
        Assert.Equal((short)-2, decoded.S);
        Assert.Equal((ushort)40000, decoded.US);
        Assert.Equal(-123456, decoded.I);
        Assert.Equal(4_000_000_000u, decoded.UI);
        Assert.Equal(3.25f, decoded.F);
        Assert.Equal(2.718281828459045, decoded.D);
    }

    [Fact]
    public void Codec_StringRoundTrip_SupportsAllThreeEncodings()
    {
        var value = new StringStruct
        {
            Name = "MAIN",
            Label = "主电机",
            Code = "ABCD"
        };

        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<StringStruct>();
        byte[] encoded = StructEncoder.Encode(value, snapshot);
        Assert.Equal(46, encoded.Length);

        StringStruct decoded = StructDecoder.Decode<StringStruct>(encoded, snapshot);
        Assert.Equal("MAIN", decoded.Name);
        Assert.Equal("主电机", decoded.Label);
        Assert.Equal("ABCD", decoded.Code);
    }

    [Fact]
    public void Codec_BoolUsesNonZeroSemantics()
    {
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<NumericStruct>();
        var value = new NumericStruct { Flag = false };
        byte[] encoded = StructEncoder.Encode(value, snapshot);
        encoded[0] = 0x02;

        NumericStruct decoded = StructDecoder.Decode<NumericStruct>(encoded, snapshot);
        Assert.True(decoded.Flag);
    }

    [Fact]
    public void Codec_ReadonlyField_IsWrittenBack()
    {
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<ReadonlyStruct>();
        byte[] encoded = StructEncoder.Encode(new ReadonlyStruct(42), snapshot);
        ReadonlyStruct decoded = StructDecoder.Decode<ReadonlyStruct>(encoded, snapshot);
        Assert.Equal(42, decoded.Value);
    }

    [Fact]
    public void Codec_FieldValueErrors_AreMapped()
    {
        StructLayoutSnapshot numeric = StructLayoutResolver.Resolve<NumericStruct>();
        StructFieldDescriptor shortField = numeric.Fields.Single(field => field.Name == nameof(NumericStruct.S));
        StructFieldDescriptor byteField = numeric.Fields.Single(field => field.Name == nameof(NumericStruct.B));
        StructLayoutSnapshot strings = StructLayoutResolver.Resolve<StringStruct>();
        StructFieldDescriptor nameField = strings.Fields.Single(field => field.Name == nameof(StringStruct.Name));

        S7ClientException outOfRange = Assert.Throws<S7ClientException>(
            () => StructEncoder.EncodeField(40000, shortField, new byte[shortField.Length], "NumericStruct"));
        Assert.Equal(S7ErrorCode.ValueOutOfRange, outOfRange.ErrorCode);

        S7ClientException byteRange = Assert.Throws<S7ClientException>(
            () => StructEncoder.EncodeField(400, byteField, new byte[byteField.Length], "NumericStruct"));
        Assert.Equal(S7ErrorCode.ValueOutOfRange, byteRange.ErrorCode);

        S7ClientException mismatch = Assert.Throws<S7ClientException>(
            () => StructEncoder.EncodeField("x", shortField, new byte[shortField.Length], "NumericStruct"));
        Assert.Equal(S7ErrorCode.ValueTypeMismatch, mismatch.ErrorCode);

        S7ClientException nonAscii = Assert.Throws<S7ClientException>(
            () => StructEncoder.EncodeField("中文", nameField, new byte[nameField.Length], "StringStruct"));
        Assert.Equal(S7ErrorCode.StringEncodingInvalid, nonAscii.ErrorCode);

        S7ClientException tooLong = Assert.Throws<S7ClientException>(
            () => StructEncoder.EncodeField(new string('A', 17), nameField, new byte[nameField.Length], "StringStruct"));
        Assert.Equal(S7ErrorCode.StringCapacityInvalid, tooLong.ErrorCode);
    }

    [Fact]
    public void Decoder_ShortDataAndInvalidWString_AreRejected()
    {
        StructLayoutSnapshot numeric = StructLayoutResolver.Resolve<NumericStruct>();
        S7ClientException tooShort = Assert.Throws<S7ClientException>(
            () => StructDecoder.Decode<NumericStruct>(new byte[25], numeric));
        Assert.Equal(S7ErrorCode.ResponseTooShort, tooShort.ErrorCode);

        StructLayoutSnapshot strings = StructLayoutResolver.Resolve<StringStruct>();
        S7ClientException wstring = Assert.Throws<S7ClientException>(
            () => StructDecoder.Decode<StringStruct>(new byte[strings.TotalLength], strings));
        Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, wstring.ErrorCode);
    }

    [Fact]
    public void Address_OnlyDataBlockByteAddress_IsAccepted()
    {
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<NumericStruct>();

        AssertAddressError("MW10", snapshot.TotalLength, S7ErrorCode.StructAddressNotSupported);
        AssertAddressError("DB1.DBX0.3", snapshot.TotalLength, S7ErrorCode.StructAddressNotSupported);
        AssertAddressError("DB1.DBW0", snapshot.TotalLength, S7ErrorCode.StructAddressNotSupported);
        AssertAddressError("DB1.DBB65530", 8, S7ErrorCode.AddressOutOfRange);

        var dbZero = new S7Address
        {
            Area = S7Area.DataBlock,
            DbNumber = 0,
            StartByte = 0,
            Width = S7AccessWidth.Byte,
            Expression = "DB0.DBB0"
        };
        S7ClientException dbZeroException = Assert.Throws<S7ClientException>(
            () => StructAddressValidator.Validate(in dbZero, snapshot.TotalLength));
        Assert.Equal(S7ErrorCode.StructAddressNotSupported, dbZeroException.ErrorCode);

        S7Address lastValid = S7Address.Parse("DB1.DBB65528");
        StructAddressValidator.Validate(in lastValid, 8);
    }

    [Fact]
    public async Task ReadPath_UsesSingleReadAndReturnsStruct()
    {
        var fake = new FakeTcpTransport();
        var value = new NumericStruct
        {
            Flag = true,
            B = 9,
            S = -12,
            US = 50000,
            I = 1234567,
            UI = 3_000_000_000,
            F = 1.5f,
            D = -2.25
        };
        StructLayoutSnapshot snapshot = StructLayoutResolver.Resolve<NumericStruct>();
        byte[] encoded = StructEncoder.Encode(value, snapshot);
        encoded.CopyTo(fake.Memory, 0);

        await using S7Client client = new S7Client(CreateOptions(), transport: fake);
        await client.ConnectAsync();

        S7StructReadResult<NumericStruct> result = await client.ReadStructAsync<NumericStruct>("DB2.DBB0");

        Assert.Equal(26, result.BytesOccupied);
        Assert.True(result.Value.Flag);
        Assert.Equal((short)-12, result.Value.S);
        Assert.Equal("DB2.DBB0", result.Address.Expression);
        Assert.Equal(S7AccessWidth.Byte, result.Address.Width);
        Assert.Null(result.Address.BitNumber);
        Assert.Equal(0, fake.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x05));
        Assert.Equal(1, fake.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x04));
    }

    [Fact]
    public async Task WritePath_OverwritesBytesAndRoundTrips()
    {
        var fake = new FakeTcpTransport();
        fake.Memory[0] = 0x80;
        var value = new NumericStruct
        {
            Flag = true,
            B = 3,
            S = -7,
            US = 60000,
            I = -7654321,
            UI = 1234567890,
            F = 4.5f,
            D = 6.75
        };

        await using S7Client client = new S7Client(CreateOptions(), transport: fake);
        await client.ConnectAsync();

        int written = await client.WriteStructAsync("DB2.DBB0", value);
        Assert.Equal(26, written);
        Assert.Equal(1, fake.Memory[0]);

        S7StructReadResult<NumericStruct> read = await client.ReadStructAsync<NumericStruct>("DB2.DBB0");
        Assert.Equal(3, read.Value.B);
        Assert.Equal((short)-7, read.Value.S);
        Assert.Equal((ushort)60000, read.Value.US);
        Assert.Equal(-7654321, read.Value.I);
        Assert.Equal(1234567890u, read.Value.UI);
        Assert.Equal(4.5f, read.Value.F);
        Assert.Equal(6.75, read.Value.D);
    }

    [Fact]
    public async Task StringPath_RoundTripsAcrossEncodings()
    {
        var fake = new FakeTcpTransport();
        var value = new StringStruct
        {
            Name = "MOTOR-1",
            Label = "主轴",
            Code = "ABC"
        };

        await using S7Client client = new S7Client(CreateOptions(), transport: fake);
        await client.ConnectAsync();

        int written = await client.WriteStructAsync("DB2.DBB32", value);
        Assert.Equal(46, written);

        S7StructReadResult<StringStruct> read = await client.ReadStructAsync<StringStruct>("DB2.DBB32");
        Assert.Equal("MOTOR-1", read.Value.Name);
        Assert.Equal("主轴", read.Value.Label);
        Assert.Equal("ABC", read.Value.Code);
    }

    [Fact]
    public async Task InvalidInputs_AreRejectedWithoutSendingFrames()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(CreateOptions(), transport: fake);
        await client.ConnectAsync();
        int baseline = fake.ReceivedFrames.Count;

        S7ClientException nonAscii = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteStructAsync("DB2.DBB0", new StringStruct
            {
                Name = "中文",
                Label = "主电机",
                Code = "ABC"
            }));
        Assert.Equal(S7ErrorCode.StringEncodingInvalid, nonAscii.ErrorCode);
        Assert.Equal(baseline, fake.ReceivedFrames.Count);

        S7ClientException nonDb = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadStructAsync<NumericStruct>("MW10"));
        Assert.Equal(S7ErrorCode.StructAddressNotSupported, nonDb.ErrorCode);
        Assert.Equal(baseline, fake.ReceivedFrames.Count);

        S7ClientException layout = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadStructAsync<ArrayFieldStruct>("DB2.DBB0"));
        Assert.Equal(S7ErrorCode.DataTypeNotSupported, layout.ErrorCode);
        Assert.Equal(baseline, fake.ReceivedFrames.Count);
    }

    [Fact]
    public async Task CrossPdu_SplitsReadAndWriteIntoChunks()
    {
        var fake = new FakeTcpTransport();
        var value = new ChunkStruct
        {
            F01 = 1,
            F02 = 2,
            F03 = 3,
            F04 = 4,
            F05 = 5,
            F06 = 6,
            F07 = 7,
            F08 = 8,
            F09 = 9,
            F10 = 10,
            F11 = 11,
            F12 = 12,
            F13 = 13,
            F14 = 14,
            F15 = 15,
            F16 = 16,
            F17 = 17,
            F18 = 18,
            F19 = 19,
            F20 = 20,
            Text = "CHUNK"
        };

        await using S7Client client = new S7Client(CreateOptions(requestedPduSize: 240), transport: fake);
        await client.ConnectAsync();

        int written = await client.WriteStructAsync("DB2.DBB0", value);
        Assert.Equal(282, written);
        Assert.Equal(2, fake.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x05));

        S7StructReadResult<ChunkStruct> read = await client.ReadStructAsync<ChunkStruct>("DB2.DBB0");
        Assert.Equal(282, read.BytesOccupied);
        Assert.Equal(2, fake.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x04));
        Assert.Equal(1, read.Value.F01);
        Assert.Equal(20, read.Value.F20);
        Assert.Equal("CHUNK", read.Value.Text);
    }

    [Fact]
    public async Task StructuredLogs_ContainMetadataButNotValues()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(CreateOptions(), logger, fake);
        await client.ConnectAsync();

        var value = new StringStruct
        {
            Name = "SECRET-NAME",
            Label = "秘密",
            Code = "ABC"
        };
        await client.WriteStructAsync("DB2.DBB0", value);

        S7LogEntry entry = logger.Entries.Last(entry => entry.Operation == "WriteStruct");
        Assert.Equal(nameof(StringStruct), entry.StructTypeName);
        Assert.Equal(3, entry.MappedFieldCount);
        Assert.Equal(46, entry.Length);
        Assert.True(entry.Succeeded);

        foreach (System.Reflection.PropertyInfo property in typeof(S7LogEntry).GetProperties())
        {
            if (property.PropertyType == typeof(string) && property.GetValue(entry) is string text)
            {
                Assert.DoesNotContain("SECRET-NAME", text);
                Assert.DoesNotContain("秘密", text);
            }
        }
    }

    private static void AssertLayoutError<T>(S7ErrorCode expectedCode)
        where T : struct
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => StructLayoutResolver.Resolve<T>());
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
    }

    private static void AssertAddressError(string expression, int length, S7ErrorCode expectedCode)
    {
        S7Address address = S7Address.Parse(expression);
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => StructAddressValidator.Validate(in address, length));
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
    }

    private static S7ConnectionOptions CreateOptions(int requestedPduSize = 480)
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            WriteTimeout = TimeSpan.FromSeconds(2),
            RequestedPduSize = requestedPduSize
        };
    }

    private struct NumericStruct
    {
        public bool Flag;
        public byte B;
        public short S;
        public ushort US;
        public int I;
        public uint UI;
        public float F;
        public double D;
    }

    private struct NoPaddingStruct
    {
        public byte A;
        public byte B;
        public byte C;
        public double D;

        public NoPaddingStruct(byte a, byte b, byte c, double d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PackStruct
    {
        public byte A;
        public double B;

        public PackStruct(byte a, double b)
        {
            A = a;
            B = b;
        }
    }

    private struct StringStruct
    {
        [S7StringField(16)]
        public string Name;

        [S7StringField(8, DataType = S7DataType.S7WString)]
        public string Label;

        [S7StringField(8, DataType = S7DataType.String)]
        public string Code;
    }

    private struct MissingCapacityStruct
    {
        public string Name;

        public MissingCapacityStruct(string name)
        {
            Name = name;
        }
    }

    private struct DuplicateCapacityStruct
    {
        [S7StringField(8)]
        [S7StringField(16)]
        public string Name;

        public DuplicateCapacityStruct(string name)
        {
            Name = name;
        }
    }

    private struct PrivateAnnotatedStruct
    {
        [S7StringField(8)]
        private string _hidden;

        public int Value;

        public PrivateAnnotatedStruct(string hidden)
        {
            _hidden = hidden;
            Value = 0;
        }

        public int HiddenLength => _hidden.Length;
    }

    private struct PropertyAnnotatedStruct
    {
        [S7StringField(8)]
        public string Name { get; set; }

        public int Value;

        public PropertyAnnotatedStruct(string name, int value)
        {
            Name = name;
            Value = value;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ExplicitStruct
    {
        [FieldOffset(0)]
        public int Value;

        public ExplicitStruct(int value)
        {
            Value = value;
        }
    }

    private struct EmptyStruct
    {
    }

    private struct ArrayFieldStruct
    {
        public int[] Values;

        public ArrayFieldStruct(int[] values)
        {
            Values = values;
        }
    }

    private struct NestedFieldStruct
    {
        public NoPaddingStruct Inner;

        public NestedFieldStruct(NoPaddingStruct inner)
        {
            Inner = inner;
        }
    }

    private struct DateTimeFieldStruct
    {
        public DateTime Timestamp;

        public DateTimeFieldStruct(DateTime timestamp)
        {
            Timestamp = timestamp;
        }
    }

    private struct DecimalFieldStruct
    {
        public decimal Amount;

        public DecimalFieldStruct(decimal amount)
        {
            Amount = amount;
        }
    }

    private enum SampleEnum
    {
        A = 0
    }

    private struct EnumFieldStruct
    {
        public SampleEnum Value;

        public EnumFieldStruct(SampleEnum value)
        {
            Value = value;
        }
    }

    private struct MixedVisibilityStruct
    {
        private int Hidden;
        private static int StaticHidden;

        public int PublicValue;

        public MixedVisibilityStruct(int hidden, int value, int staticHidden)
        {
            Hidden = hidden;
            PublicValue = value;
            StaticHidden = staticHidden;
        }

        public int HiddenValue => Hidden;

        public static int StaticHiddenValue => StaticHidden;
    }

    private struct ReadonlyStruct
    {
        public readonly int Value;

        public ReadonlyStruct(int value)
        {
            Value = value;
        }
    }

    private struct ChunkStruct
    {
        public int F01;
        public int F02;
        public int F03;
        public int F04;
        public int F05;
        public int F06;
        public int F07;
        public int F08;
        public int F09;
        public int F10;
        public int F11;
        public int F12;
        public int F13;
        public int F14;
        public int F15;
        public int F16;
        public int F17;
        public int F18;
        public int F19;
        public int F20;

        [S7StringField(200)]
        public string Text;
    }
}