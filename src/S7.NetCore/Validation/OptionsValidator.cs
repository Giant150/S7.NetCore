using System.Net;

namespace S7.NetCore.Internal;

/// <summary>
/// 连接参数校验器。所有非法输入在发起网络请求前被拒绝。
/// </summary>
internal static class OptionsValidator
{
    /// <summary>
    /// 校验连接参数与重连策略。
    /// </summary>
    /// <param name="options">连接参数。</param>
    /// <exception cref="S7ClientException">参数非法时抛出。</exception>
    internal static void Validate(S7ConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IpAddress) || !IPAddress.TryParse(options.IpAddress, out _))
        {
            throw Invalid(S7ErrorCode.InvalidAddress, "IpAddress 必须是合法的 IPv4 或 IPv6 地址，且不接受空值或主机名。");
        }

        if (options.Port is < 1 or > 65535)
        {
            throw Invalid(S7ErrorCode.InvalidPort, $"Port 必须在 1 至 65535 之间，当前值：{options.Port}。");
        }

        if (!Enum.IsDefined(options.CpuType))
        {
            throw Invalid(S7ErrorCode.InvalidCpuType, "CpuType 必须是受支持的 CPU 型号枚举值。");
        }

        if (options.Rack is < 0 or > ProtocolConstants.MaxRackSlot)
        {
            throw Invalid(S7ErrorCode.InvalidRackSlot, $"Rack 必须在 0 至 {ProtocolConstants.MaxRackSlot} 之间，当前值：{options.Rack}。");
        }

        if (options.Slot is < 0 or > ProtocolConstants.MaxRackSlot)
        {
            throw Invalid(S7ErrorCode.InvalidRackSlot, $"Slot 必须在 0 至 {ProtocolConstants.MaxRackSlot} 之间，当前值：{options.Slot}。");
        }

        ValidateTimeout(options.ConnectTimeout, 1, 120, "ConnectTimeout");
        ValidateTimeout(options.ReadTimeout, 1, 300, "ReadTimeout");
        ValidateTimeout(options.WriteTimeout, 1, 300, "WriteTimeout");

        if (options.RequestedPduSize is < ProtocolConstants.MinPduSize or > ProtocolConstants.MaxRequestedPduSize)
        {
            throw Invalid(
                S7ErrorCode.InvalidPduSize,
                $"RequestedPduSize 必须在 {ProtocolConstants.MinPduSize} 至 {ProtocolConstants.MaxRequestedPduSize} 之间，当前值：{options.RequestedPduSize}。");
        }

        if (options.MaxQueuedRequests is < 1 or > 1024)
        {
            throw Invalid(S7ErrorCode.InvalidLength, $"MaxQueuedRequests 必须在 1 至 1024 之间，当前值：{options.MaxQueuedRequests}。");
        }

        ValidateReconnectPolicy(options.Reconnect);
    }

    private static void ValidateReconnectPolicy(S7ReconnectPolicy? policy)
    {
        if (policy is null)
        {
            throw InvalidPolicy("Reconnect 策略不得为 null。");
        }

        if (policy.MaxAttempts is < 0 or > 100)
        {
            throw InvalidPolicy($"MaxAttempts 必须在 0 至 100 之间（0 表示不限），当前值：{policy.MaxAttempts}。");
        }

        if (policy.InitialDelay < TimeSpan.FromMilliseconds(100) || policy.InitialDelay > TimeSpan.FromSeconds(30))
        {
            throw Invalid(
                S7ErrorCode.InvalidTimeout,
                $"Reconnect.InitialDelay 必须在 100 毫秒至 30 秒之间，当前值：{policy.InitialDelay.TotalMilliseconds} 毫秒。");
        }

        if (policy.MaxDelay < TimeSpan.FromSeconds(1) || policy.MaxDelay > TimeSpan.FromSeconds(300))
        {
            throw Invalid(
                S7ErrorCode.InvalidTimeout,
                $"Reconnect.MaxDelay 必须在 1 至 300 秒之间，当前值：{policy.MaxDelay.TotalSeconds} 秒。");
        }

        if (policy.MaxDelay < policy.InitialDelay)
        {
            throw InvalidPolicy(
                $"Reconnect.MaxDelay 不得小于 InitialDelay，当前值：MaxDelay={policy.MaxDelay.TotalSeconds} 秒，InitialDelay={policy.InitialDelay.TotalSeconds} 秒。");
        }

        if (policy.WaitTimeout < TimeSpan.Zero || policy.WaitTimeout > TimeSpan.FromSeconds(120))
        {
            throw Invalid(
                S7ErrorCode.InvalidTimeout,
                $"Reconnect.WaitTimeout 必须在 0 至 120 秒之间（0 表示不等待），当前值：{policy.WaitTimeout.TotalSeconds} 秒。");
        }

        if (policy.BackoffMultiplier is < 1.0 or > 10.0)
        {
            throw InvalidPolicy($"Reconnect.BackoffMultiplier 必须在 1.0 至 10.0 之间，当前值：{policy.BackoffMultiplier}。");
        }

        if (policy.JitterFactor is < 0.0 or > 0.5)
        {
            throw InvalidPolicy($"Reconnect.JitterFactor 必须在 0 至 0.5 之间，当前值：{policy.JitterFactor}。");
        }
    }

    private static void ValidateTimeout(TimeSpan value, int minSeconds, int maxSeconds, string name)
    {
        if (value < TimeSpan.FromSeconds(minSeconds) || value > TimeSpan.FromSeconds(maxSeconds))
        {
            throw Invalid(
                S7ErrorCode.InvalidTimeout,
                $"{name} 必须在 {minSeconds} 至 {maxSeconds} 秒之间，当前值：{value.TotalSeconds} 秒。");
        }
    }

    private static S7ClientException Invalid(S7ErrorCode code, string message)
    {
        return new S7ClientException(S7ErrorCategory.InvalidArgument, code, S7OperationStage.Validate, message);
    }

    private static S7ClientException InvalidPolicy(string message)
    {
        return Invalid(S7ErrorCode.InvalidReconnectPolicy, message);
    }
}