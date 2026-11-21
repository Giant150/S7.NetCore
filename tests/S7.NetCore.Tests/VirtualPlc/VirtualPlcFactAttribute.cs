namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 依赖真实虚拟 PLC 的测试事实：发现阶段探测不可用时静态标记为跳过。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class VirtualPlcFactAttribute : FactAttribute
{
    public VirtualPlcFactAttribute()
    {
        (bool ready, string reason) = VirtualPlcAvailability.Check();
        if (!ready)
        {
            Skip = $"虚拟 PLC 未就绪：{reason}。请确认本机 S7 服务端已启动，"
                + "或通过 S7_VIRTUAL_PLC_HOST/PORT/CPU/RACK/SLOT 覆盖端点。";
        }
    }
}