using System.Globalization;
using System.Net;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc.Server;

internal enum VirtualPlcServerMode
{
    Embedded,
    External,
    Off
}

internal sealed record VirtualPlcServerSettings(
    VirtualPlcServerMode Mode,
    string Host,
    int Port,
    S7CpuType CpuType,
    int Rack,
    int Slot,
    bool AllowNonLoopback,
    int MaxClients)
{
    internal VirtualPlcEndpoint Endpoint => new(Host, Port, CpuType, Rack, Slot);

    internal static VirtualPlcServerSettings Parse()
    {
        string modeText = Get("S7_VIRTUAL_PLC_SERVER", "embedded").ToLowerInvariant();
        VirtualPlcServerMode mode = modeText switch
        {
            "embedded" => VirtualPlcServerMode.Embedded,
            "external" => VirtualPlcServerMode.External,
            "off" => VirtualPlcServerMode.Off,
            _ => throw new FormatException(
                $"S7_VIRTUAL_PLC_SERVER 非法：'{modeText}'，允许 embedded/external/off。")
        };

        string host = Get("S7_VIRTUAL_PLC_HOST", "127.0.0.1");
        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            throw new FormatException($"S7_VIRTUAL_PLC_HOST 非法：'{host}'，必须是 IP 地址。");
        }

        int port = ParseInt(
            "S7_VIRTUAL_PLC_PORT",
            Get("S7_VIRTUAL_PLC_PORT", "1102"),
            1024,
            65535);

        if (port == 102)
        {
            throw new FormatException("虚拟 PLC 端口不得为 102；默认使用 1102。");
        }

        if (mode == VirtualPlcServerMode.Embedded && !IPAddress.IsLoopback(address))
        {
            throw new FormatException(
                $"嵌入式服务器必须绑定回环地址，当前 S7_VIRTUAL_PLC_HOST='{host}'。");
        }

        S7CpuType cpuType = ParseCpuType(Get("S7_VIRTUAL_PLC_CPU", "S71200"));
        int rack = ParseInt("S7_VIRTUAL_PLC_RACK", Get("S7_VIRTUAL_PLC_RACK", "0"), 0, 15);
        int slot = ParseInt("S7_VIRTUAL_PLC_SLOT", Get("S7_VIRTUAL_PLC_SLOT", "1"), 0, 15);
        bool allowNonLoopback = ParseBool(
            "S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK",
            Get("S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK", "false"));

        int maxClients = ParseInt(
            "S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS",
            Get("S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS", "1"),
            1,
            8);

        return new VirtualPlcServerSettings(
            mode,
            host,
            port,
            cpuType,
            rack,
            slot,
            allowNonLoopback,
            maxClients);
    }

    private static string Get(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ParseInt(string name, string text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value < minimum
            || value > maximum)
        {
            throw new FormatException(
                $"环境变量 {name} 非法：'{text}'，允许 {minimum}～{maximum}。");
        }

        return value;
    }

    private static bool ParseBool(string name, string text)
    {
        if (bool.TryParse(text, out bool value))
        {
            return value;
        }

        throw new FormatException($"环境变量 {name} 非法：'{text}'，允许 true/false。");
    }

    private static S7CpuType ParseCpuType(string text)
    {
        if (!Enum.TryParse(text, ignoreCase: true, out S7CpuType cpuType) || !Enum.IsDefined(cpuType))
        {
            throw new FormatException($"环境变量 S7_VIRTUAL_PLC_CPU 非法：'{text}'。");
        }

        return cpuType;
    }
}