using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedChunkBoundaryTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task T_B01_ReadS7String252Bytes_SplitsByPdu()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        S7Address address = S7Address.Parse("DB2.DBB8192");

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.ChunkStringStart,
            TypedDb2Segments.ChunkStringLength,
            async () =>
            {
                _ = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB8192",
                        S7DataType.S7String,
                        S7Value.FromString("HELLO"),
                        TypedDb2Segments.ChunkStringCapacity),
                    BudgetToken);

                session.Logger.Entries.Clear();
                session.Logger.Records.Clear();
                int frameBase = session.Transport.Frames.Count;

                ReadOnlyMemory<byte> raw = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.ChunkStringStart,
                        TypedDb2Segments.ChunkStringLength),
                    BudgetToken);

                RecordedFrame[] readFrames = session.Transport.Frames.Skip(frameBase).ToArray();
                ChunkAssertions.AssertReadChunks(
                    session.Logger,
                    pduSize,
                    address,
                    S7DataType.S7String,
                    TypedDb2Segments.ChunkStringLength,
                    raw,
                    readFrames);

                if (pduSize == 240)
                {
                    ChunkBoundary[] chunks = ChunkFrameInspector.ReadChunks(readFrames).ToArray();
                    Assert.Equal(2, chunks.Length);
                    Assert.Equal([0, 222], chunks.Select(
                        chunk => chunk.StartByte - address.StartByte).ToArray());
                    Assert.Equal([222, 30], chunks.Select(chunk => chunk.Length).ToArray());
                }

                S7TypedReadResult typed = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB8192",
                        S7DataType.S7String,
                        TypedDb2Segments.ChunkStringCapacity),
                    BudgetToken);

                Assert.Equal("HELLO", typed.Value.AsString());
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_B02_WriteS7String252Bytes_SplitsByPdu()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        S7Address address = S7Address.Parse("DB2.DBB8192");

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.ChunkStringStart,
            TypedDb2Segments.ChunkStringLength,
            async () =>
            {
                session.Logger.Entries.Clear();
                session.Logger.Records.Clear();
                int frameBase = session.Transport.Frames.Count;

                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB8192",
                        S7DataType.S7String,
                        S7Value.FromString("HELLO"),
                        TypedDb2Segments.ChunkStringCapacity),
                    BudgetToken);

                RecordedFrame[] writeFrames = session.Transport.Frames.Skip(frameBase).ToArray();
                ChunkAssertions.AssertWriteChunks(
                    session.Logger,
                    pduSize,
                    address,
                    S7DataType.S7String,
                    TypedDb2Segments.ChunkStringLength,
                    written,
                    writeFrames);

                if (pduSize == 240)
                {
                    Assert.Equal(252, written);
                    ChunkBoundary[] chunks = ChunkFrameInspector.WriteChunks(writeFrames).ToArray();
                    Assert.Equal(2, chunks.Length);
                    Assert.Equal([0, 205], chunks.Select(
                        chunk => chunk.StartByte - address.StartByte).ToArray());
                    Assert.Equal([205, 47], chunks.Select(chunk => chunk.Length).ToArray());
                }

                S7TypedReadResult typed = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB8192",
                        S7DataType.S7String,
                        TypedDb2Segments.ChunkStringCapacity),
                    BudgetToken);

                Assert.Equal("HELLO", typed.Value.AsString());
            },
            BudgetToken);
    }

    [VirtualPlcFact]
    public async Task T_W14_WriteAndReadWString504Bytes_SplitsByPdu()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        S7Address address = S7Address.Parse("DB2.DBB8448");

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.ChunkWStringStart,
            TypedDb2Segments.ChunkWStringLength,
            async () =>
            {
                session.Logger.Entries.Clear();
                session.Logger.Records.Clear();
                int frameBase = session.Transport.Frames.Count;

                int written = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB8448",
                        S7DataType.S7WString,
                        S7Value.FromString("HELLO"),
                        TypedDb2Segments.ChunkWStringCapacity),
                    BudgetToken);

                RecordedFrame[] writeFrames = session.Transport.Frames.Skip(frameBase).ToArray();
                ChunkAssertions.AssertWriteChunks(
                    session.Logger,
                    pduSize,
                    address,
                    S7DataType.S7WString,
                    TypedDb2Segments.ChunkWStringLength,
                    written,
                    writeFrames);

                if (pduSize == 240)
                {
                    Assert.Equal(504, written);
                    ChunkBoundary[] writeChunks = ChunkFrameInspector.WriteChunks(writeFrames).ToArray();
                    Assert.Equal(3, writeChunks.Length);
                    Assert.Equal([0, 205, 410], writeChunks.Select(
                        chunk => chunk.StartByte - address.StartByte).ToArray());
                    Assert.Equal([205, 205, 94], writeChunks.Select(chunk => chunk.Length).ToArray());
                }

                session.Logger.Entries.Clear();
                session.Logger.Records.Clear();
                frameBase = session.Transport.Frames.Count;

                ReadOnlyMemory<byte> raw = await session.Client.ReadBytesAsync(
                    new S7ReadRequest(
                        S7Area.DataBlock,
                        2,
                        TypedDb2Segments.ChunkWStringStart,
                        TypedDb2Segments.ChunkWStringLength),
                    BudgetToken);

                RecordedFrame[] readFrames = session.Transport.Frames.Skip(frameBase).ToArray();
                ChunkAssertions.AssertReadChunks(
                    session.Logger,
                    pduSize,
                    address,
                    S7DataType.S7WString,
                    TypedDb2Segments.ChunkWStringLength,
                    raw,
                    readFrames);

                if (pduSize == 240)
                {
                    ChunkBoundary[] readChunks = ChunkFrameInspector.ReadChunks(readFrames).ToArray();
                    Assert.Equal(3, readChunks.Length);
                    Assert.Equal([0, 222, 444], readChunks.Select(
                        chunk => chunk.StartByte - address.StartByte).ToArray());
                    Assert.Equal([222, 222, 60], readChunks.Select(chunk => chunk.Length).ToArray());
                }

                S7TypedReadResult typed = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB8448",
                        S7DataType.S7WString,
                        TypedDb2Segments.ChunkWStringCapacity),
                    BudgetToken);

                Assert.Equal("HELLO", typed.Value.AsString());
            },
            BudgetToken);
    }
}