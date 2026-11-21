using System.Globalization;
using System.Net;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 虚拟 PLC 端点与连接参数。默认 127.0.0.1:1102 + S71200/0/1，可由环境变量覆盖。
/// </summary>
internal readonly record struct VirtualPlcEndpoint(string Host, int Port, S7CpuType CpuType, int Rack, int Slot)
{
    /// <summary>规格默认端点。</summary>
    internal static VirtualPlcEndpoint Default { get; } = new("127.0.0.1", 1102, S7CpuType.S71200, 0, 1);

    /// <summary>端点是否为回环地址。</summary>
    internal bool IsLoopback => IPAddress.TryParse(Host, out IPAddress? address) && IPAddress.IsLoopback(address);

    /// <summary>生成组件连接参数。</summary>
    internal S7ConnectionOptions ToOptions()
    {
        return new S7ConnectionOptions
        {
            IpAddress = Host,
            Port = Port,
            CpuType = CpuType,
            Rack = Rack,
            Slot = Slot,
            RequestedPduSize = 480,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ReadTimeout = TimeSpan.FromSeconds(2),
            WriteTimeout = TimeSpan.FromSeconds(2)
        };
    }

    /// <summary>
    /// 从环境变量解析端点；未设置时回退默认值。
    /// </summary>
    /// <exception cref="FormatException">任一配置项非法。</exception>
    internal static VirtualPlcEndpoint FromEnvironment()
    {
        string host = Get("S7_VIRTUAL_PLC_HOST", Default.Host);
        if (!IPAddress.TryParse(host, out _))
        {
            throw new FormatException($"环境变量 S7_VIRTUAL_PLC_HOST 非法：{host}，必须是可解析的 IP 地址。");
        }

        int port = ParseInt("S7_VIRTUAL_PLC_PORT", Get("S7_VIRTUAL_PLC_PORT", Default.Port.ToString(CultureInfo.InvariantCulture)), 1024, 65535);

        if (port == 102)
        {
            throw new FormatException("虚拟 PLC 端口不得为 102；默认使用 1102。");
        }

        int rack = ParseInt("S7_VIRTUAL_PLC_RACK", Get("S7_VIRTUAL_PLC_RACK", Default.Rack.ToString(CultureInfo.InvariantCulture)), 0, 15);
        int slot = ParseInt("S7_VIRTUAL_PLC_SLOT", Get("S7_VIRTUAL_PLC_SLOT", Default.Slot.ToString(CultureInfo.InvariantCulture)), 0, 15);
        S7CpuType cpuType = ParseCpu(Get("S7_VIRTUAL_PLC_CPU", Default.CpuType.ToString()));

        return new VirtualPlcEndpoint(host, port, cpuType, rack, slot);
    }

    private static string Get(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ParseInt(string name, string text, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value < min
            || value > max)
        {
            throw new FormatException($"环境变量 {name} 非法：{text}，允许范围 {min} 至 {max}。");
        }

        return value;
    }

    private static S7CpuType ParseCpu(string text)
    {
        if (!Enum.TryParse(text, ignoreCase: true, out S7CpuType cpuType) || !Enum.IsDefined(cpuType))
        {
            throw new FormatException($"环境变量 S7_VIRTUAL_PLC_CPU 非法：{text}。");
        }

        return cpuType;
    }
}