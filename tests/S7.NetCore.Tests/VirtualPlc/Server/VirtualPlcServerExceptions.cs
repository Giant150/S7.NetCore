namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed class VirtualPlcEnvironmentSkipException : Exception
{
    internal VirtualPlcEnvironmentSkipException(VirtualPlcSkipReason reason, string detail)
        : base($"虚拟 PLC 环境跳过（{reason}）：{detail}")
    {
        Reason = reason;
        Detail = detail;
    }

    internal VirtualPlcSkipReason Reason { get; }

    internal string Detail { get; }
}

internal sealed class VirtualPlcFailureException : Exception
{
    internal VirtualPlcFailureException(
        string stage,
        string detail,
        int? nativeCode = null,
        Exception? innerException = null)
        : base($"虚拟 PLC 实现失败（阶段：{stage}）：{detail}", innerException)
    {
        Stage = stage;
        Detail = detail;
        NativeCode = nativeCode;
    }

    internal string Stage { get; }

    internal string Detail { get; }

    internal int? NativeCode { get; }
}

internal sealed class VirtualPlcContentionException : Exception
{
    internal VirtualPlcContentionException(string message)
        : base(message)
    {
    }
}