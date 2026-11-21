using S7.NetCore;
using Xunit.Abstractions;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedWriteRoundTripTests : VirtualPlcTestBase
{
    private readonly ITestOutputHelper _output;

    public TypedWriteRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualPlcFact]
    public async Task T_W01_Db2Word_IntRoundTripAndViews()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        async Task<S7Value> Read(string expression, S7DataType dataType, int capacity = 0)
        {
            return (await session.Client.ReadValueAsync(
                new S7TypedReadRequest(expression, dataType, capacity),
                BudgetToken)).Value;
        }

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 0, 2, async () =>
        {
            int written = await session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBW0", S7DataType.Int, S7Value.FromInt16(-12345)),
                BudgetToken);

            Assert.Equal(2, written);
            Assert.Equal((short)-12345, (await Read("DB2.DBW0", S7DataType.Int)).AsInt16());
            Assert.Equal((ushort)53191, (await Read("DB2.DBW0", S7DataType.Word)).AsUInt16());
            Assert.Equal((byte)207, (await Read("DB2.DBB0", S7DataType.Byte)).AsByte());
            Assert.Equal((byte)199, (await Read("DB2.DBB1", S7DataType.Byte)).AsByte());
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W02_Db2DWord_RealRoundTripAndBitPattern()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 0, 4, async () =>
        {
            int written = await session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBD0", S7DataType.Real, S7Value.FromSingle(3.14159f)),
                BudgetToken);
            Assert.Equal(4, written);

            float real = (await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD0", S7DataType.Real),
                BudgetToken)).Value.AsSingle();
            uint dword = (await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD0", S7DataType.DWord),
                BudgetToken)).Value.AsUInt32();

            Assert.Equal(BitConverter.SingleToUInt32Bits(3.14159f), BitConverter.SingleToUInt32Bits(real));
            Assert.Equal(BitConverter.SingleToUInt32Bits(real), dword);
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W03_Db2DInt_RoundTripAndUnsignedView()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 8, 4, async () =>
        {
            int written = await session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBD8", S7DataType.DInt, S7Value.FromInt32(-2000000000)),
                BudgetToken);
            Assert.Equal(4, written);

            int dint = (await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD8", S7DataType.DInt),
                BudgetToken)).Value.AsInt32();
            uint dword = (await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD8", S7DataType.DWord),
                BudgetToken)).Value.AsUInt32();

            Assert.Equal(-2000000000, dint);
            Assert.Equal(2294967296u, dword);
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W04_Db2LReal_RoundTripAndBitPattern()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(session.Client, 2, 4, 8, async () =>
        {
            const double expected = 2.718281828459045;
            int written = await session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBD4", S7DataType.LReal, S7Value.FromDouble(expected)),
                BudgetToken);
            Assert.Equal(8, written);

            S7TypedReadResult result = await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBD4", S7DataType.LReal),
                BudgetToken);

            Assert.Equal(8, result.BytesOccupied);
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected),
                BitConverter.DoubleToInt64Bits(result.Value.AsDouble()));
        }, BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W05_Db2S7String_HeaderAndRoundTrip()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.S7StringStart,
            TypedDb2Segments.S7StringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB16",
                        S7DataType.S7String,
                        S7Value.FromString("S7-PLC"),
                        TypedDb2Segments.S7StringCapacity),
                    BudgetToken);

                Assert.Equal(18, written);
                Assert.Equal((byte)16, (await session.Client.ReadValueAsync(
                    new S7TypedReadRequest("DB2.DBB16", S7DataType.Byte),
                    BudgetToken)).Value.AsByte());
                Assert.Equal((byte)6, (await session.Client.ReadValueAsync(
                    new S7TypedReadRequest("DB2.DBB17", S7DataType.Byte),
                    BudgetToken)).Value.AsByte());

                S7TypedReadResult result = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB16",
                        S7DataType.S7String,
                        TypedDb2Segments.S7StringCapacity),
                    BudgetToken);

                Assert.Equal("S7-PLC", result.Value.AsString());
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W06_Db2CString_TerminatorAndRoundTrip()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.CStringStart,
            TypedDb2Segments.CStringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB40",
                        S7DataType.String,
                        S7Value.FromString("HELLO"),
                        TypedDb2Segments.CStringCapacity),
                    BudgetToken);

                Assert.Equal(8, written);
                Assert.Equal((byte)0, (await session.Client.ReadValueAsync(
                    new S7TypedReadRequest("DB2.DBB45", S7DataType.Byte),
                    BudgetToken)).Value.AsByte());

                S7TypedReadResult result = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB40",
                        S7DataType.String,
                        TypedDb2Segments.CStringCapacity),
                    BudgetToken);

                Assert.Equal("HELLO", result.Value.AsString());
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W07_Db2BitWrite_IsolatesTargetBit()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.BitStart,
            TypedDb2Segments.BitLength,
            async snapshot =>
            {
                byte original = snapshot[0];
                int readsBefore = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;

                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest("DB2.DBX4096.3", S7DataType.Bool, S7Value.FromBool(true)),
                    BudgetToken);

                Assert.Equal(1, written);
                int readsAfter = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;
                Assert.Equal(readsBefore, readsAfter);

                byte afterTrue = (await session.Client.ReadValueAsync(
                    new S7TypedReadRequest("DB2.DBB4096", S7DataType.Byte),
                    BudgetToken)).Value.AsByte();

                Assert.Equal(original | 0x08, afterTrue);
                Assert.Equal(original & ~0x08, afterTrue & ~0x08);

                readsBefore = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;
                written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest("DB2.DBX4096.3", S7DataType.Bool, S7Value.FromBool(false)),
                    BudgetToken);

                Assert.Equal(1, written);
                readsAfter = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;
                Assert.Equal(readsBefore, readsAfter);

                byte afterFalse = (await session.Client.ReadValueAsync(
                    new S7TypedReadRequest("DB2.DBB4096", S7DataType.Byte),
                    BudgetToken)).Value.AsByte();

                Assert.Equal(original & ~0x08, afterFalse);

                BitWriteBoundary[] bitWrites = ChunkFrameInspector
                    .BitWrites(session.Transport.Frames)
                    .ToArray();

                Assert.Equal(2, bitWrites.Length);
                Assert.All(bitWrites, boundary => Assert.Equal(TypedDb2Segments.BitStart, boundary.StartByte));
                Assert.All(bitWrites, boundary => Assert.Equal(3, boundary.BitNumber));
                Assert.True(bitWrites[0].Value);
                Assert.False(bitWrites[1].Value);

                foreach (RecordedFrame frame in session.Transport.Frames.Where(
                    frame => frame.Data.Length == 36 && frame.Data[22] == 0x01))
                {
                    Assert.Equal((byte)0x01, frame.Data[22]);
                    Assert.Equal((byte)0x03, frame.Data[32]);
                    Assert.True(frame.Data[35] <= 1);
                    _output.WriteLine($"位写帧: {Convert.ToHexString(frame.Data)}");
                }
            },
            BudgetToken);
    }
}