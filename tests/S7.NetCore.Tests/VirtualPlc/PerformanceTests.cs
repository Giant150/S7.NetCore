using System.Diagnostics;
using System.Runtime.InteropServices;
using S7.NetCore;
using Xunit.Abstractions;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class PerformanceTests : VirtualPlcTestBase
{
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualPlcFact]
    public async Task V_P01_Read1024_P95Within500Milliseconds()
    {
        using CancellationTokenSource scope = CreateTimeoutScope(TimeSpan.FromSeconds(180));
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(scope.Token);

        int pduSize = session.Client.NegotiatedPduSize;
        _ = await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 1024),
            scope.Token);

        var samples = new List<double>(20);

        for (int i = 0; i < 20; i++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 1024),
                scope.Token);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        samples.Sort();
        double p95 = samples[18];
        Report("V-P01", pduSize, samples, p95);

        Assert.True(p95 > 0, "P95 必须大于 0。");
        Assert.True(
            p95 <= 500,
            $"1024 字节读 P95={p95:F1}ms 超过 500ms。样本：{string.Join(",", samples.Select(value => value.ToString("F1")))}");
    }

    [VirtualPlcFact]
    public async Task V_P02_ConnectDisconnect_P95Within3Seconds()
    {
        var samples = new List<double>(20);
        int pduSize = 0;

        for (int i = 0; i < 20; i++)
        {
            long start = Stopwatch.GetTimestamp();

            await using (VirtualPlcSession session = CreateSession())
            {
                await session.Client.ConnectAsync(BudgetToken);
                pduSize = session.Client.NegotiatedPduSize;
                await session.Client.DisconnectAsync(BudgetToken);
            }

            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        samples.Sort();
        double p95 = samples[18];
        Report("V-P02", pduSize, samples, p95);

        Assert.True(p95 > 0, "P95 必须大于 0。");
        Assert.True(
            p95 <= 3000,
            $"连接+断开 P95={p95:F1}ms 超过 3000ms。样本：{string.Join(",", samples.Select(value => value.ToString("F1")))}");
    }

    private void Report(string caseName, int pduSize, List<double> samples, double p95)
    {
        _output.WriteLine(
            $"{caseName}: endpoint={Endpoint.Host}:{Endpoint.Port}, pdu={pduSize}, "
            + $"os={RuntimeInformation.OSDescription}, runtime={Environment.Version}, "
            + $"min={samples[0]:F1}ms, p50={samples[9]:F1}ms, p95={p95:F1}ms, max={samples[^1]:F1}ms");
        _output.WriteLine($"{caseName} samples(ms): {string.Join(",", samples.Select(value => value.ToString("F1")))}");
        _output.WriteLine("注意：仅本机回环基线，不等同于局域网真实 PLC 指标。");
    }
}