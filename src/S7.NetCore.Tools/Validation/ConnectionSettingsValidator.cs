using System.Globalization;
using System.Net;
using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Validation;

public static class ConnectionSettingsValidator
{
    public static IReadOnlyList<FieldValidationError> Validate(ConnectionSettingsModel settings)
    {
        var errors = new List<FieldValidationError>();

        if (string.IsNullOrWhiteSpace(settings.IpAddress) || !IPAddress.TryParse(settings.IpAddress, out _))
        {
            errors.Add(new FieldValidationError(nameof(settings.IpAddress), S7ErrorCode.InvalidAddress, "请输入合法的 IPv4/IPv6 地址。"));
        }

        if (settings.Port is < 1 or > 65535)
        {
            errors.Add(new FieldValidationError(nameof(settings.Port), S7ErrorCode.InvalidPort, "端口必须在 1 至 65535 之间。"));
        }

        if (!Enum.IsDefined(settings.CpuType))
        {
            errors.Add(new FieldValidationError(nameof(settings.CpuType), S7ErrorCode.InvalidCpuType, "请选择受支持的 CPU 型号。"));
        }

        if (settings.Rack is < 0 or > 15)
        {
            errors.Add(new FieldValidationError(nameof(settings.Rack), S7ErrorCode.InvalidRackSlot, "机架号必须在 0 至 15 之间。"));
        }

        if (settings.Slot is < 0 or > 15)
        {
            errors.Add(new FieldValidationError(nameof(settings.Slot), S7ErrorCode.InvalidRackSlot, "槽位号必须在 0 至 15 之间。"));
        }

        ValidateTsap(settings, errors);
        ValidateTimeout(settings.ConnectTimeoutSeconds, 1, 120, nameof(settings.ConnectTimeoutSeconds), errors);
        ValidateTimeout(settings.ReadTimeoutSeconds, 1, 300, nameof(settings.ReadTimeoutSeconds), errors);
        ValidateTimeout(settings.WriteTimeoutSeconds, 1, 300, nameof(settings.WriteTimeoutSeconds), errors);

        if (settings.RequestedPduSize is < 240 or > 960)
        {
            errors.Add(new FieldValidationError(nameof(settings.RequestedPduSize), S7ErrorCode.InvalidPduSize, "PDU 必须在 240 至 960 之间。"));
        }

        if (settings.MaxQueuedRequests is < 1 or > 1024)
        {
            errors.Add(new FieldValidationError(nameof(settings.MaxQueuedRequests), S7ErrorCode.InvalidLength, "请求队列必须在 1 至 1024 之间。"));
        }

        ReconnectSettingsModel reconnect = settings.Reconnect;
        if (reconnect.MaxAttempts is < 0 or > 100)
        {
            errors.Add(new FieldValidationError("Reconnect.MaxAttempts", S7ErrorCode.InvalidReconnectPolicy, "最大尝试次数必须在 0 至 100 之间。"));
        }

        if (reconnect.InitialDelayMilliseconds is < 100 or > 30000)
        {
            errors.Add(new FieldValidationError("Reconnect.InitialDelayMilliseconds", S7ErrorCode.InvalidTimeout, "首次退避必须在 100 至 30000 毫秒之间。"));
        }

        if (reconnect.MaxDelaySeconds is < 1 or > 300)
        {
            errors.Add(new FieldValidationError("Reconnect.MaxDelaySeconds", S7ErrorCode.InvalidTimeout, "退避上限必须在 1 至 300 秒之间。"));
        }

        if (reconnect.MaxDelaySeconds < reconnect.InitialDelayMilliseconds / 1000.0)
        {
            errors.Add(new FieldValidationError("Reconnect.MaxDelaySeconds", S7ErrorCode.InvalidReconnectPolicy, "退避上限不得小于首次退避。"));
        }

        if (reconnect.WaitTimeoutSeconds is < 0 or > 120)
        {
            errors.Add(new FieldValidationError("Reconnect.WaitTimeoutSeconds", S7ErrorCode.InvalidTimeout, "等待窗口必须在 0 至 120 秒之间。"));
        }

        if (reconnect.BackoffMultiplier is < 1.0 or > 10.0)
        {
            errors.Add(new FieldValidationError("Reconnect.BackoffMultiplier", S7ErrorCode.InvalidReconnectPolicy, "退避倍数必须在 1.0 至 10.0 之间。"));
        }

        if (reconnect.JitterFactor is < 0.0 or > 0.5)
        {
            errors.Add(new FieldValidationError("Reconnect.JitterFactor", S7ErrorCode.InvalidReconnectPolicy, "抖动比例必须在 0 至 0.5 之间。"));
        }

        return errors;
    }

    public static S7ConnectionOptions ToOptions(ConnectionSettingsModel settings)
    {
        Tsap? local = CreateTsap(settings.LocalTsapFirst, settings.LocalTsapSecond);
        Tsap? remote = CreateTsap(settings.RemoteTsapFirst, settings.RemoteTsapSecond);
        ReconnectSettingsModel reconnect = settings.Reconnect;

        return new S7ConnectionOptions
        {
            IpAddress = settings.IpAddress.Trim(),
            CpuType = settings.CpuType,
            Port = settings.Port,
            Rack = settings.Rack,
            Slot = settings.Slot,
            LocalTsap = local,
            RemoteTsap = remote,
            ConnectTimeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
            ReadTimeout = TimeSpan.FromSeconds(settings.ReadTimeoutSeconds),
            WriteTimeout = TimeSpan.FromSeconds(settings.WriteTimeoutSeconds),
            RequestedPduSize = settings.RequestedPduSize,
            MaxQueuedRequests = settings.MaxQueuedRequests,
            EnableProtocolTrace = settings.EnableProtocolTrace,
            Reconnect = new S7ReconnectPolicy
            {
                AutoReconnectEnabled = reconnect.AutoReconnectEnabled,
                MaxAttempts = reconnect.MaxAttempts,
                InitialDelay = TimeSpan.FromMilliseconds(reconnect.InitialDelayMilliseconds),
                MaxDelay = TimeSpan.FromSeconds(reconnect.MaxDelaySeconds),
                BackoffMultiplier = reconnect.BackoffMultiplier,
                JitterFactor = reconnect.JitterFactor,
                WaitTimeout = TimeSpan.FromSeconds(reconnect.WaitTimeoutSeconds),
                ReconnectOnTimeout = reconnect.ReconnectOnTimeout,
                ReplayUncertainWrites = reconnect.ReplayUncertainWrites
            }
        };
    }

    private static Tsap? CreateTsap(byte? first, byte? second)
    {
        return first.HasValue && second.HasValue ? new Tsap(first.Value, second.Value) : null;
    }

    private static void ValidateTsap(ConnectionSettingsModel settings, List<FieldValidationError> errors)
    {
        if (HasSingleTsapByte(settings.LocalTsapFirst, settings.LocalTsapSecond))
        {
            errors.Add(new FieldValidationError("LocalTsap", S7ErrorCode.InvalidRackSlot, "本地 TSAP 两个字节必须同时填写或同时留空。"));
        }

        if (HasSingleTsapByte(settings.RemoteTsapFirst, settings.RemoteTsapSecond))
        {
            errors.Add(new FieldValidationError("RemoteTsap", S7ErrorCode.InvalidRackSlot, "远端 TSAP 两个字节必须同时填写或同时留空。"));
        }
    }

    private static bool HasSingleTsapByte(byte? first, byte? second)
    {
        return first.HasValue != second.HasValue;
    }

    private static void ValidateTimeout(
        int seconds,
        int minimum,
        int maximum,
        string field,
        List<FieldValidationError> errors)
    {
        if (seconds < minimum || seconds > maximum)
        {
            errors.Add(new FieldValidationError(field, S7ErrorCode.InvalidTimeout, $"超时必须在 {minimum} 至 {maximum} 秒之间。"));
        }
    }
}