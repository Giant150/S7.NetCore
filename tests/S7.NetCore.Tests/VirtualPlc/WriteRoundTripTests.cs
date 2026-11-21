using System.Runtime.ExceptionServices;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class WriteRoundTripTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task V_W01_Db2_1024Bytes_RoundTripsAndRestores()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        byte[] before = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 2, 0, 1024),
            BudgetToken)).ToArray();

        await ProtectedWrite.WriteVerifyAndRestoreAsync(
            session.Client,
            S7Area.DataBlock,
            2,
            0,
            1024,
            BudgetToken);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 2, 0, 1024),
            BudgetToken)).ToArray();

        Assert.True(
            before.SequenceEqual(after),
            "5.5.1.8a 的 DB1 已按 D-01 映射为 DB2：1024 字节写回环后 DB2 [0,1024) 未恢复到写前快照。");
    }

    [VirtualPlcFact]
    public async Task V_W02_Memory_WriteRoundTripsAndRestores()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int writeLength = session.Client.NegotiatedPduSize - ProtocolConstants.ReadResponseOverhead;

        byte[] before = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, writeLength),
            BudgetToken)).ToArray();

        await ProtectedWrite.WriteVerifyAndRestoreAsync(
            session.Client,
            S7Area.Memory,
            0,
            0,
            writeLength,
            BudgetToken);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, writeLength),
            BudgetToken)).ToArray();

        Assert.Equal(before, after);
    }

    [VirtualPlcFact]
    public async Task V_W03_Output_WriteRoundTripsAndRestores()
    {
        if (!Endpoint.IsLoopback && !VirtualPlcEnvironment.AllowNonLoopbackWrites)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "非回环端点写 Q 区需显式设置 S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK=true。");
        }

        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int writeLength = session.Client.NegotiatedPduSize - ProtocolConstants.ReadResponseOverhead;

        byte[] before = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Output, 0, 0, writeLength),
            BudgetToken)).ToArray();

        await ProtectedWrite.WriteVerifyAndRestoreAsync(
            session.Client,
            S7Area.Output,
            0,
            0,
            writeLength,
            BudgetToken);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Output, 0, 0, writeLength),
            BudgetToken)).ToArray();

        Assert.Equal(before, after);
    }

    [VirtualPlcFact]
    public async Task V_W04_Db2_1024Bytes_SplitsByNegotiatedPdu()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        int writeChunkSize = pduSize - ProtocolConstants.WriteRequestOverhead;

        byte[] snapshot = await RegionSnapshot.CaptureAsync(
            session.Client,
            S7Area.DataBlock,
            2,
            0,
            1024,
            BudgetToken);

        byte[] pattern = KnownPattern.Create(0, 1024);
        Exception? bodyException = null;

        try
        {
            int written = await session.Client.WriteBytesAsync(
                new S7WriteRequest(S7Area.DataBlock, 2, 0, pattern),
                BudgetToken);

            ChunkAssertions.AssertWriteChunks(
                session.Logger,
                pduSize,
                S7Area.DataBlock,
                2,
                0,
                1024,
                written,
                session.Transport.Frames);

            ReadOnlyMemory<byte> readBack = await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 2, 0, 1024),
                BudgetToken);

            Assert.Equal(pattern, readBack.ToArray());

            ChunkBoundary[] chunks = ChunkFrameInspector
                .WriteChunks(session.Transport.Frames)
                .Take((1024 + writeChunkSize - 1) / writeChunkSize)
                .ToArray();

            int expectedCount = (1024 + writeChunkSize - 1) / writeChunkSize;
            Assert.All(
                chunks,
                chunk => Assert.Equal((chunk.Index - 1) * writeChunkSize, chunk.StartByte));

            if (pduSize == 240)
            {
                Assert.Equal([0, 205, 410, 615, 820], chunks.Select(chunk => chunk.StartByte));
                Assert.Equal(5, expectedCount);
            }
        }
        catch (Exception ex)
        {
            bodyException = ex;
        }

        SnapshotRestoreResult restore = await RegionSnapshot.RestoreAndVerifyAsync(
            session.Client,
            S7Area.DataBlock,
            2,
            0,
            snapshot,
            CancellationToken.None);

        if (!restore.Succeeded)
        {
            throw new SnapshotRestoreException(
                S7Area.DataBlock,
                2,
                0,
                1024,
                restore.FailureReason ?? "未知原因",
                bodyException);
        }

        if (bodyException is not null)
        {
            ExceptionDispatchInfo.Capture(bodyException).Throw();
        }
    }

    [VirtualPlcFact]
    public async Task V_W05_InputArea_WriteIsRejectedWithoutStateChange()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        byte[] before = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Input, 0, 0, 8),
            BudgetToken)).ToArray();

        int writeFramesBefore = ChunkFrameInspector.WriteChunks(session.Transport.Frames).Count;

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = session.Client.WriteBytesAsync(new S7WriteRequest(S7Area.Input, 0, 0, new byte[8])); });

        Assert.Equal(S7ErrorCode.AreaNotSupported, exception.ErrorCode);
        Assert.Equal(writeFramesBefore, ChunkFrameInspector.WriteChunks(session.Transport.Frames).Count);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Input, 0, 0, 8),
            BudgetToken)).ToArray();

        Assert.Equal(before, after);
    }

    [VirtualPlcFact]
    public async Task V_W06_RestoreVerification_RestoresSnapshotExactly()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        byte[] snapshot = await RegionSnapshot.CaptureAsync(
            session.Client,
            S7Area.DataBlock,
            2,
            0,
            1024,
            BudgetToken);

        _ = await session.Client.WriteBytesAsync(
            new S7WriteRequest(S7Area.DataBlock, 2, 0, KnownPattern.Create(0, 1024)),
            BudgetToken);

        SnapshotRestoreResult restore = await RegionSnapshot.RestoreAndVerifyAsync(
            session.Client,
            S7Area.DataBlock,
            2,
            0,
            snapshot,
            CancellationToken.None);

        Assert.True(restore.Succeeded, restore.FailureReason);

        byte[] after = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 2, 0, 1024),
            BudgetToken)).ToArray();

        Assert.Equal(snapshot, after);
    }
}