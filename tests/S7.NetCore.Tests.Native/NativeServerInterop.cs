using System.Runtime.CompilerServices;
using Snap7;

namespace S7.NetCore.Tests.Native;

/// <summary>
/// 原生 S7Server 私有句柄读取。销毁统一走 S7Server.Destroy()。
/// </summary>
public static class NativeServerInterop
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Server")]
    private static extern ref IntPtr GetServerField(S7Server server);

    /// <summary>读取 S7Server 私有句柄。</summary>
    public static IntPtr GetHandle(S7Server server)
    {
        return GetServerField(server);
    }
}