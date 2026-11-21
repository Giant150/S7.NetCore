using S7.NetCore;
using S7.NetCore.Tests.VirtualPlc.Server;
using Xunit.Abstractions;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class ServerDataTests : VirtualPlcTestBase
{
    private readonly ITestOutputHelper _output;

    public ServerDataTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualPlcFact]
    public async Task S_D01_Db1_ReadIsDeterministicAcrossRestart()
    {
        byte before;

        await using (VirtualPlcSession session = CreateSession())
        {
            await session.Client.ConnectAsync(BudgetToken);
            before = (await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 1),
                BudgetToken)).Span[0];
        }

        await VirtualPlcServerFixture.Current!.RestartForLifecycleTestAsync();

        byte after;

        await using (VirtualPlcSession session = CreateSession())
        {
            await session.Client.ConnectAsync(BudgetToken);
            after = (await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 1),
                BudgetToken)).Span[0];
        }

        Assert.Equal((byte)0x0B, before);
        Assert.Equal(before, after);
    }

    [VirtualPlcFact]
    public async Task S_D02_AllRegisteredAreasStartWith0x0B_AfterRestart()
    {
        await VirtualPlcServerFixture.Current!.RestartForLifecycleTestAsync();

        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        (S7Area Area, int DbNumber)[] targets =
        [
            (S7Area.Input, 0),
            (S7Area.Output, 0),
            (S7Area.Memory, 0),
            (S7Area.DataBlock, 1)
        ];

        foreach ((S7Area area, int dbNumber) in targets)
        {
            byte value = (await session.Client.ReadBytesAsync(
                new S7ReadRequest(area, dbNumber, 0, 1),
                BudgetToken)).Span[0];

            Assert.True(
                value == 0x0B,
                $"{Describe(area, dbNumber, 0, 1)} 首字节期望 0x0B，实际 0x{value:X2}。");
        }
    }

    [VirtualPlcFact]
    public void S_M03_SessionEndpointMatchesFixtureEndpoint()
    {
        VirtualPlcServerFixture fixture = VirtualPlcServerFixture.Current!;
        VirtualPlcObservedBaseline? baseline = fixture.Snapshot.Baseline;
        _output.WriteLine(
            $"PDU={fixture.Snapshot.NegotiatedPduSize}, R={baseline?.ReadChunkSize}, "
            + $"W={baseline?.WriteChunkSize}, startup={baseline?.StartupMilliseconds:F1}ms, "
            + $"areas={fixture.RegisteredAreas.Count}");

        Assert.Equal(fixture.Endpoint, Endpoint);
        Assert.Equal(1102, Endpoint.Port);
        Assert.Equal(6, fixture.RegisteredAreas.Count);
        Assert.All(fixture.RegisteredAreas, area => Assert.Equal(65535, area.Capacity));
    }
}