using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 环境探测结果。
/// </summary>
internal readonly record struct VirtualPlcProbe(bool Ready, S7OperationStage Stage, string Reason);

/// <summary>
/// 虚拟 PLC 就绪探测与运行时跳过。跳过判定只允许发生在本类。
/// </summary>
internal static class VirtualPlcEnvironment
{
    /// <summary>非回环端点写用例显式开关。</summary>
    internal static bool AllowNonLoopbackWrites => string.Equals(
        Environment.GetEnvironmentVariable("S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 要求虚拟 PLC 就绪，未就绪时以跳过结束用例。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（仅测试缺陷时触发取消）。</param>
    /// <returns>已就绪的端点。</returns>
    internal static async Task<VirtualPlcEndpoint> RequireReadyAsync(CancellationToken cancellationToken)
    {
        VirtualPlcEndpoint endpoint;
        try
        {
            endpoint = VirtualPlcEndpoint.FromEnvironment();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                $"虚拟 PLC 端点配置非法：{ex.Message}。可检查 S7_VIRTUAL_PLC_HOST/PORT/CPU/RACK/SLOT。");
        }

        VirtualPlcProbe probe = await ProbeAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (!probe.Ready)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                $"虚拟 PLC 未就绪：端点 {endpoint.Host}:{endpoint.Port}，失败阶段 {probe.Stage}，原因：{probe.Reason}。"
                + "请确认本机 S7 服务端已启动，或通过 S7_VIRTUAL_PLC_HOST/PORT/CPU/RACK/SLOT 覆盖端点。");
        }

        return endpoint;
    }

    /// <summary>
    /// 使用独立客户端探测端点，探测阶段不写入任何数据。
    /// </summary>
    /// <param name="endpoint">端点。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探测结果。</returns>
    /// <exception cref="OperationCanceledException">探测期间调用方取消（测试自身缺陷，必须失败）。</exception>
    internal static async Task<VirtualPlcProbe> ProbeAsync(
        VirtualPlcEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        S7ConnectionOptions options = endpoint.ToOptions() with
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            WriteTimeout = TimeSpan.FromSeconds(2)
        };

        await using var client = new S7Client(options);

        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            return new VirtualPlcProbe(true, S7OperationStage.Connect, string.Empty);
        }
        catch (S7ClientException ex) when (ex.Category == S7ErrorCategory.Cancelled)
        {
            throw;
        }
        catch (S7ClientException ex)
        {
            return new VirtualPlcProbe(
                false,
                ex.Stage,
                $"{ex.Category}/{ex.ErrorCode}: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}