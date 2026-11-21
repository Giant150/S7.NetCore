using S7.NetCore;
using S7.NetCore.Internal;
using S7.NetCore.Tests.VirtualPlc.Server;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 真实链路测试基类：每个测试方法独立创建会话，端点来自嵌入式服务器夹具快照。
/// </summary>
public abstract class VirtualPlcTestBase : IAsyncLifetime
{
    private CancellationTokenSource? _budget;

    /// <summary>已就绪的端点。</summary>
    internal VirtualPlcEndpoint Endpoint { get; private set; }

    /// <summary>用例总预算令牌。</summary>
    internal CancellationToken BudgetToken => _budget?.Token ?? CancellationToken.None;

    /// <summary>服务器侧实测基线。</summary>
    internal VirtualPlcObservedBaseline? ObservedBaseline =>
        VirtualPlcServerFixture.Current?.Snapshot.Baseline;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _budget = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        VirtualPlcServerFixture fixture = await VirtualPlcServerFixture
            .EnsureStartedAsync()
            .ConfigureAwait(false);

        VirtualPlcServerSnapshot snapshot = fixture.Snapshot;

        switch (snapshot.Kind)
        {
            case VirtualPlcServerSnapshotKind.Ready:
                Endpoint = snapshot.Endpoint;
                break;

            case VirtualPlcServerSnapshotKind.EnvironmentSkip:
                throw Xunit.Sdk.SkipException.ForSkip(
                    $"虚拟 PLC 环境跳过（{snapshot.Reason}）：{snapshot.Detail}");

            case VirtualPlcServerSnapshotKind.Failure:
                throw new Xunit.Sdk.XunitException(
                    $"虚拟 PLC 服务器托管失败（{snapshot.Stage}）：{snapshot.Detail}");

            default:
                throw new Xunit.Sdk.XunitException($"未知夹具快照状态：{snapshot.Kind}");
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _budget?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>创建独立会话（真实 TcpTransport + 录制装饰器 + 全开日志）。</summary>
    internal VirtualPlcSession CreateSession()
    {
        var transport = new RecordingTcpTransport(new TcpTransport());
        var logger = new TestLogger();
        var client = new S7Client(Endpoint.ToOptions(), logger, transport);
        return new VirtualPlcSession(client, transport, logger);
    }

    /// <summary>创建带超时的子预算，用于限制单个阶段的耗时。</summary>
    internal CancellationTokenSource CreateTimeoutScope(TimeSpan timeout)
    {
        CancellationTokenSource scope = CancellationTokenSource.CreateLinkedTokenSource(BudgetToken);
        scope.CancelAfter(timeout);
        return scope;
    }

    /// <summary>断言失败消息的统一上下文拼装。</summary>
    internal static string Describe(S7Area area, int dbNumber, int startByte, int length)
    {
        return $"区域 {area}，DB {dbNumber}，起始 {startByte}，长度 {length}";
    }
}

/// <summary>
/// 单个虚拟 PLC 用例的独立会话上下文。
/// </summary>
internal sealed class VirtualPlcSession : IAsyncDisposable
{
    internal VirtualPlcSession(S7Client client, RecordingTcpTransport transport, TestLogger logger)
    {
        Client = client;
        Transport = transport;
        Logger = logger;
    }

    internal S7Client Client { get; }

    internal RecordingTcpTransport Transport { get; }

    internal TestLogger Logger { get; }

    public ValueTask DisposeAsync()
    {
        return Client.DisposeAsync();
    }
}