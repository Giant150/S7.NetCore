using System.Net;
using System.Net.Sockets;
using S7.NetCore.Tests.VirtualPlc.Server;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 测试发现阶段的虚拟 PLC 可用性探测，结果在进程内缓存。
/// 只做静态判定：不创建服务器、不绑定端口。
/// </summary>
internal static class VirtualPlcAvailability
{
    private static readonly Lazy<(bool Ready, string Reason)> ProbeResult = new(
        Probe,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static (bool Ready, string Reason) Check()
    {
        return ProbeResult.Value;
    }

    private static (bool Ready, string Reason) Probe()
    {
        try
        {
            VirtualPlcServerSettings settings = VirtualPlcServerSettings.Parse();

            if (settings.Mode == VirtualPlcServerMode.Off)
            {
                return (false, "S7_VIRTUAL_PLC_SERVER=off");
            }

            if (settings.Mode == VirtualPlcServerMode.External)
            {
                return IsListening(settings.Host, settings.Port)
                    ? (true, string.Empty)
                    : (false, $"外部虚拟 PLC 不可达：{settings.Host}:{settings.Port}。");
            }

            VirtualPlcNativeLibraryProbe library = VirtualPlcNativeLibrary.Probe(settings.LibraryPath);
            if (!library.Available)
            {
                return (false, library.Detail);
            }

            if (IsListening(settings.Host, settings.Port))
            {
                return (false, $"端口 {settings.Port} 已被其他进程占用。");
            }

            return (true, string.Empty);
        }
        catch (FormatException exception)
        {
            return (false, exception.Message);
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private static bool IsListening(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var scope = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            client.ConnectAsync(host, port, scope.Token).GetAwaiter().GetResult();
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}