using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class StructRoundTripTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task S_R01_NumericStruct_RoundTripsAtDb2Offset256()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        var value = new NumericStruct
        {
            Flag = true,
            B = 17,
            S = -12345,
            US = 54321,
            I = 1234567,
            UI = 3_000_000_000,
            F = 3.5f,
            D = -2.718281828459045
        };

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 256, 26, async () =>
        {
            int written = await session.Client.WriteStructAsync("DB2.DBB256", value, BudgetToken);
            Assert.Equal(26, written);

            S7StructReadResult<NumericStruct> read = await session.Client.ReadStructAsync<NumericStruct>(
                "DB2.DBB256",
                BudgetToken);

            Assert.Equal(26, read.BytesOccupied);
            Assert.True(read.Value.Flag);
            Assert.Equal((byte)17, read.Value.B);
            Assert.Equal((short)-12345, read.Value.S);
            Assert.Equal((ushort)54321, read.Value.US);
            Assert.Equal(1234567, read.Value.I);
            Assert.Equal(3_000_000_000u, read.Value.UI);
            Assert.Equal(3.5f, read.Value.F);
            Assert.Equal(-2.718281828459045, read.Value.D);

            S7TypedReadResult crossView = await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD262", S7DataType.DInt),
                BudgetToken);
            Assert.Equal(1234567, crossView.Value.AsInt32());
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task S_R02_StringStruct_RoundTripsAtDb2Offset512()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        var value = new StringStruct
        {
            Name = "MOTOR-1",
            Label = "主轴",
            Code = "ABC"
        };

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 512, 46, async () =>
        {
            int written = await session.Client.WriteStructAsync("DB2.DBB512", value, BudgetToken);
            Assert.Equal(46, written);

            S7StructReadResult<StringStruct> read = await session.Client.ReadStructAsync<StringStruct>(
                "DB2.DBB512",
                BudgetToken);

            Assert.Equal(46, read.BytesOccupied);
            Assert.Equal("MOTOR-1", read.Value.Name);
            Assert.Equal("主轴", read.Value.Label);
            Assert.Equal("ABC", read.Value.Code);
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task S_R03_UnregisteredDb_MapsToPlcError()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.Client.ReadStructAsync<NumericStruct>("DB4.DBB0", BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, exception.ErrorCode);
        Assert.Equal((byte)0x0A, exception.PlcReturnCode);
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

    private struct StringStruct
    {
        [S7StringField(16)]
        public string Name;

        [S7StringField(8, DataType = S7DataType.S7WString)]
        public string Label;

        [S7StringField(8, DataType = S7DataType.String)]
        public string Code;
    }
}