using System.Diagnostics;
using System.Runtime.InteropServices;
using S7.NetCore;
using Xunit.Abstractions;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedPerformanceTests : VirtualPlcTestBase
{
    private readonly ITestOutputHelper _output;

    public TypedPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualPlcFact]
    public async Task T_P01_TypedReadP95Within100Milliseconds()
    {
        using CancellationTokenSource scope = CreateTimeoutScope(TimeSpan.FromSeconds(180));
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(scope.Token);

        int pduSize = session.Client.NegotiatedPduSize;
        short before = (await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBW0", S7DataType.Int),
            scope.Token)).Value.AsInt16();

        _ = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBW0", S7DataType.Int),
            scope.Token);

        var samples = new List<double>(20);
        for (int i = 0; i < 20; i++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = await session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBW0", S7DataType.Int),
                scope.Token);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        short after = (await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBW0", S7DataType.Int),
            scope.Token)).Value.AsInt16();

        samples.Sort();
        double p95 = samples[18];

        _output.WriteLine(
            $"T-P01: endpoint={Endpoint.Host}:{Endpoint.Port}, pdu={pduSize}, "
            + $"os={RuntimeInformation.OSDescription}, runtime={Environment.Version}, "
            + $"min={samples[0]:F1}ms, p50={samples[9]:F1}ms, p95={p95:F1}ms, max={samples[^1]:F1}ms");
        _output.WriteLine($"T-P01 samples(ms): {string.Join(",", samples.Select(value => value.ToString("F1")))}");
        _output.WriteLine("注意：仅本机回环基线，不等同于局域网真实 PLC 指标。");

        Assert.Equal(before, after);
        Assert.True(p95 > 0, "P95 必须大于 0。");
        Assert.True(
            p95 <= 100,
            $"DB2.DBW0 Int 类型化读取 P95={p95:F1}ms 超过 100ms。"
            + $"样本：{string.Join(",", samples.Select(value => value.ToString("F1")))}");
    }

    [VirtualPlcFact]
    public async Task T_P02_WStringRead_P95Within100Milliseconds()
    {
        using CancellationTokenSource scope = CreateTimeoutScope(TimeSpan.FromSeconds(180));
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(scope.Token);

        int pduSize = session.Client.NegotiatedPduSize;

        await TypedWriteGuard.ExecuteAsync(
            session.Client,
            2,
            TypedDb2Segments.WStringStart,
            TypedDb2Segments.WStringLength,
            async () =>
            {
                _ = await session.Client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        S7Value.FromString("温度A1"),
                        TypedDb2Segments.WStringCapacity),
                    scope.Token);

                _ = await session.Client.ReadValueAsync(
                    new S7TypedReadRequest(
                        "DB2.DBB64",
                        S7DataType.S7WString,
                        TypedDb2Segments.WStringCapacity),
                    scope.Token);

                var samples = new List<double>(20);
                for (int i = 0; i < 20; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    _ = await session.Client.ReadValueAsync(
                        new S7TypedReadRequest(
                            "DB2.DBB64",
                            S7DataType.S7WString,
                            TypedDb2Segments.WStringCapacity),
                        scope.Token);
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }

                samples.Sort();
                double p95 = samples[18];

                _output.WriteLine(
                    $"T-P02: endpoint={Endpoint.Host}:{Endpoint.Port}, pdu={pduSize}, "
                    + $"os={RuntimeInformation.OSDescription}, runtime={Environment.Version}, "
                    + $"min={samples[0]:F1}ms, p50={samples[9]:F1}ms, p95={p95:F1}ms, max={samples[^1]:F1}ms");
                _output.WriteLine($"T-P02 samples(ms): {string.Join(",", samples.Select(value => value.ToString("F1")))}");
                _output.WriteLine("注意：仅本机回环基线，不等同于局域网真实 PLC 指标。");

                Assert.True(p95 > 0, "P95 必须大于 0。");
                Assert.True(
                    p95 <= 100,
                    $"DB2.DBB64 S7WString 读取 P95={p95:F1}ms 超过 100ms。"
                    + $"样本：{string.Join(",", samples.Select(value => value.ToString("F1")))}");
            },
            scope.Token);
    }
}