namespace S7.NetCore.Tests.VirtualPlc.Server;

internal enum VirtualPlcServerSnapshotKind
{
    Ready,
    EnvironmentSkip,
    Failure
}

internal sealed record VirtualPlcServerSnapshot
{
    internal VirtualPlcServerSnapshotKind Kind { get; init; }

    internal string? Reason { get; init; }

    internal string? Detail { get; init; }

    internal string? Stage { get; init; }

    internal bool IsRunning { get; init; }

    internal VirtualPlcEndpoint Endpoint { get; init; }

    internal int NegotiatedPduSize { get; init; }

    internal VirtualPlcObservedBaseline? Baseline { get; init; }

    internal IReadOnlyList<VirtualPlcRegisteredArea> RegisteredAreas { get; init; } = [];

    internal double StartupMilliseconds { get; init; }

    internal static VirtualPlcServerSnapshot Initial { get; } = new()
    {
        Kind = VirtualPlcServerSnapshotKind.EnvironmentSkip,
        Reason = VirtualPlcSkipReason.ConfigurationInvalid.ToString(),
        Detail = "测试服务器夹具尚未初始化。",
        Endpoint = VirtualPlcEndpoint.Default
    };

    internal static VirtualPlcServerSnapshot EnvironmentSkip(VirtualPlcSkipReason reason, string detail)
    {
        return new VirtualPlcServerSnapshot
        {
            Kind = VirtualPlcServerSnapshotKind.EnvironmentSkip,
            Reason = reason.ToString(),
            Detail = detail,
            Endpoint = VirtualPlcEndpoint.Default
        };
    }

    internal static VirtualPlcServerSnapshot Failure(string stage, string detail)
    {
        return new VirtualPlcServerSnapshot
        {
            Kind = VirtualPlcServerSnapshotKind.Failure,
            Stage = stage,
            Detail = detail,
            Endpoint = VirtualPlcEndpoint.Default
        };
    }
}