using System.Net.Sockets;
using S7.NetCore;
using S7.NetCore.Tests.VirtualPlc.Server;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class ServerLifecycleTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task S_L01_RestartThreeTimes_ReleasesAndRebindsPort()
    {
        VirtualPlcServerFixture fixture = VirtualPlcServerFixture.Current!;

        for (int round = 1; round <= 3; round++)
        {
            await fixture.StopForLifecycleTestAsync();

            Assert.False(
                await IsPortListeningAsync(Endpoint.Host, Endpoint.Port),
                $"第 {round} 轮停止后端口 {Endpoint.Port} 仍被监听。");

            VirtualPlcServerSnapshot snapshot = await fixture.RestartForLifecycleTestAsync();

            Assert.Equal(VirtualPlcServerSnapshotKind.Ready, snapshot.Kind);
            Assert.True(snapshot.IsRunning);

            await using VirtualPlcSession session = CreateSession();
            await session.Client.ConnectAsync(BudgetToken);
            ReadOnlyMemory<byte> data = await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 1),
                BudgetToken);

            Assert.Equal((byte)0x0B, data.Span[0]);
        }
    }

    [VirtualPlcFact]
    public async Task S_L02_StopTwice_IsIdempotent()
    {
        VirtualPlcServerFixture fixture = VirtualPlcServerFixture.Current!;

        await fixture.StopForLifecycleTestAsync();
        await fixture.StopForLifecycleTestAsync();

        Assert.False(await IsPortListeningAsync(Endpoint.Host, Endpoint.Port));

        await fixture.RestartForLifecycleTestAsync();
        Assert.True(fixture.Snapshot.IsRunning);
    }

    [VirtualPlcFact]
    public void S_L04_StartupElapsedWithinFiveSeconds()
    {
        VirtualPlcObservedBaseline? baseline = VirtualPlcServerFixture.Current!.Snapshot.Baseline;

        Assert.NotNull(baseline);
        Assert.InRange(baseline!.StartupMilliseconds, 0, 5000);
    }

    [VirtualPlcFact]
    public async Task S_L10_StoppedRegionBuffer_ThrowsAndRestarts()
    {
        VirtualPlcServerFixture fixture = VirtualPlcServerFixture.Current!;
        await fixture.StopForLifecycleTestAsync();

        try
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => fixture.CopyServerRange(S7Area.DataBlock, 1, 0, 1));

            Assert.Contains("服务器已停止", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RestartForLifecycleTestAsync();
        }
    }

    private static async Task<bool> IsPortListeningAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var scope = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await client.ConnectAsync(host, port, scope.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}