using S7.NetCore;
using Xunit.Abstractions;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedWStringTests : VirtualPlcTestBase
{
    private readonly ITestOutputHelper _output;

    public TypedWStringTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualPlcFact]
    public async Task T_W08_Db2WString_ByteLayoutUtf16Be()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        S7Value.FromString("温度A1"),
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal(36, written);

                ReadOnlyMemory<byte> raw = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.WStringStart,
                        TypedDb2Segments.WStringLength),
                    BudgetToken);

                ReadOnlySpan<byte> data = raw.Span;
                Assert.Equal(
                    new byte[]
                    {
                        0x00, 0x10, 0x00, 0x04,
                        0x6E, 0x29, 0x5E, 0xA6,
                        0x00, 0x41, 0x00, 0x31
                    },
                    data[..12].ToArray());
                Assert.All(data[12..].ToArray(), value => Assert.Equal(0, value));
                Assert.Equal((byte)0x6E, data[4]);
                Assert.Equal((byte)0x29, data[5]);

                foreach (RecordedFrame frame in session.Transport.Frames.Where(
                    frame => frame.Data.Length > 26 && frame.Data[17] == 0x05))
                {
                    int dbNumber = (frame.Data[25] << 8) | frame.Data[26];
                    Assert.NotEqual(1, dbNumber);
                }

                _output.WriteLine($"T-W08 前 12 字节: {Convert.ToHexString(data[..12])}");
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W09_Db2WString_RoundTripAndLengthField()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        S7Value.FromString("温度A1"),
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal(36, written);

                S7TypedReadResult result = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal("温度A1", result.Value.AsString());
                Assert.Equal(36, result.BytesOccupied);

                ReadOnlyMemory<byte> header = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(S7Area.DataBlock, 2, TypedDb2Segments.WStringStart, 4),
                    BudgetToken);

                int declaredLength = (header.Span[2] << 8) | header.Span[3];
                Assert.Equal(4, declaredLength);
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W10_Db2WString_SurrogatePairRoundTrip()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        S7Value.FromString("😀"),
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal(36, written);

                ReadOnlyMemory<byte> raw = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(S7Area.DataBlock, 2, TypedDb2Segments.WStringStart, 8),
                    BudgetToken);

                Assert.Equal(
                    new byte[] { 0x00, 0x10, 0x00, 0x02, 0xD8, 0x3D, 0xDE, 0x00 },
                    raw.ToArray());

                S7TypedReadResult result = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal("😀", result.Value.AsString());
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W11_Db2WString_EmptyString()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        S7Value.FromString(string.Empty),
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal(36, written);

                ReadOnlyMemory<byte> raw = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.WStringStart,
                        TypedDb2Segments.WStringLength),
                    BudgetToken);

                Assert.Equal(new byte[] { 0x00, 0x10, 0x00, 0x00 }, raw.Span[..4].ToArray());
                Assert.All(raw.Span[4..].ToArray(), value => Assert.Equal(0, value));

                S7TypedReadResult result = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        TypedDb2Segments.WStringCapacity),
                    BudgetToken);

                Assert.Equal(string.Empty, result.Value.AsString());
                Assert.Equal(36, result.BytesOccupied);
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W12_Db2WString_InvalidHeaders_Throw408()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                await session.Client.WriteBytesAsync(
                    new S7WriteRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.WStringStart,
                        new byte[] { 0x00, 0x10, 0x00, 0x11 }),
                    BudgetToken);

                S7ClientException lengthGreater = await Assert.ThrowsAsync<S7ClientException>(
                    () => session.Client.ReadValueAsync(
                        new S7TypedReadRequest(
                            "DB2.DBB64",
                            S7DataType.S7WString,
                            TypedDb2Segments.WStringCapacity),
                        BudgetToken));

                Assert.Equal(S7ErrorCategory.Protocol, lengthGreater.Category);
                Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, lengthGreater.ErrorCode);

                await session.Client.WriteBytesAsync(
                    new S7WriteRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.WStringStart,
                        new byte[] { 0x00, 0x14, 0x00, 0x04 }),
                    BudgetToken);

                S7ClientException capacityGreater = await Assert.ThrowsAsync<S7ClientException>(
                    () => session.Client.ReadValueAsync(
                        new S7TypedReadRequest(
                            "DB2.DBB64",
                            S7DataType.S7WString,
                            TypedDb2Segments.WStringCapacity),
                        BudgetToken));

                Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, capacityGreater.ErrorCode);
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W13_Db2WString_SnapshotRestoredAfterInvalidHeader()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        byte[] before = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 2, TypedDb2Segments.WStringStart, 4),
            BudgetToken)).ToArray();

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                await session.Client.WriteBytesAsync(
                    new S7WriteRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.WStringStart,
                        new byte[] { 0x00, 0x10, 0x00, 0x11 }),
                    BudgetToken);

                await Assert.ThrowsAsync<S7ClientException>(
                    () => session.Client.ReadValueAsync(
                        new S7TypedReadRequest(
                            "DB2.DBB64",
                            S7DataType.S7WString,
                            TypedDb2Segments.WStringCapacity),
                        BudgetToken));
            },
            BudgetToken);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 2, TypedDb2Segments.WStringStart, 4),
            BudgetToken)).ToArray();

        Assert.Equal(before, after);
    }
}