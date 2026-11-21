using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class SessionLifecycleTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task V_S01_Connect_EstablishesSessionAndNegotiatesPdu()
    {
        await using VirtualPlcSession session = CreateSession();

        await session.Client.ConnectAsync(BudgetToken);

        Assert.Equal(S7SessionState.Connected, session.Client.State);
        Assert.InRange(session.Client.NegotiatedPduSize, 1, 480);
        Assert.Equal(2, session.Transport.WriteCount);

        IReadOnlyList<RecordedFrame> frames = session.Transport.Frames;
        Assert.Equal(0xE0, frames[0].Data[5]);
        Assert.Equal(ProtocolConstants.S7FunctionSetup, frames[1].Data[17]);
        Assert.Empty(ChunkFrameInspector.ReadChunks(frames));
    }

    [VirtualPlcFact]
    public async Task V_S02_ConnectWhileConnected_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);
        int writeCountBefore = session.Transport.WriteCount;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ConnectAsync(BudgetToken));

        Assert.Equal(S7ErrorCode.SessionAlreadyConnected, exception.ErrorCode);
        Assert.Equal(S7SessionState.Connected, session.Client.State);
        Assert.Equal(writeCountBefore, session.Transport.WriteCount);
    }

    [VirtualPlcFact]
    public async Task V_S03_DisconnectTwice_IsIdempotent()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        await session.Client.DisconnectAsync(BudgetToken);
        await session.Client.DisconnectAsync(BudgetToken);

        Assert.Equal(S7SessionState.Disconnected, session.Client.State);
        Assert.Equal(0, session.Client.NegotiatedPduSize);
    }

    [VirtualPlcFact]
    public async Task V_S04_ReadAfterDisconnect_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);
        await session.Client.DisconnectAsync(BudgetToken);
        int writeCountBefore = session.Transport.WriteCount;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.Memory, 0, 0, 1),
                BudgetToken));

        Assert.Equal(S7ErrorCode.SessionNotEstablished, exception.ErrorCode);
        Assert.Equal(writeCountBefore, session.Transport.WriteCount);
    }

    [VirtualPlcFact]
    public async Task V_S05_ConcurrentReads_AreSerializedAndAllMatchBaseline()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        byte[] baseline = (await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 8),
            BudgetToken)).ToArray();

        int readFramesBefore = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;

        Task<ReadOnlyMemory<byte>>[] tasks = Enumerable.Range(0, 16)
            .Select(_ => session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 8),
                BudgetToken))
            .ToArray();

        ReadOnlyMemory<byte>[] results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(baseline, result.ToArray()));
        int readFramesAfter = ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count;
        Assert.Equal(16, readFramesAfter - readFramesBefore);
        Assert.Equal(S7SessionState.Connected, session.Client.State);
    }

    [VirtualPlcFact]
    public async Task V_S06_CancelledRead_DoesNotBreakSession()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        using CancellationTokenSource largeReadScope = CreateTimeoutScope(TimeSpan.FromSeconds(120));
        Task<ReadOnlyMemory<byte>> largeRead = session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 65535),
            largeReadScope.Token);

        await Task.Delay(20, BudgetToken);

        using var cancelledScope = new CancellationTokenSource();
        cancelledScope.Cancel();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 8),
                cancelledScope.Token));

        Assert.Equal(S7ErrorCategory.Cancelled, exception.Category);
        Assert.Equal(S7ErrorCode.OperationCancelled, exception.ErrorCode);

        ReadOnlyMemory<byte> largeData = await largeRead;
        Assert.Equal(65535, largeData.Length);

        ReadOnlyMemory<byte> data = await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 8),
            BudgetToken);

        Assert.Equal(8, data.Length);
        Assert.Equal(S7SessionState.Connected, session.Client.State);
    }
}