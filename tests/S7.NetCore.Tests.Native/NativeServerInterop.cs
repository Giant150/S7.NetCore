using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snap7;

namespace S7.NetCore.Tests.Native;

/// <summary>
/// 原生 S7Server 显式销毁与句柄访问。桥接单元没有公开 Destroy，只能通过本类显式释放。
/// </summary>
public static class NativeServerInterop
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "Server")]
    private static extern ref IntPtr GetServerField(S7Server server);

    [DllImport(S7Consts.Snap7LibName, EntryPoint = "Srv_Destroy")]
    private static extern int SrvDestroy(ref IntPtr server);

    /// <summary>读取 S7Server 私有句柄。</summary>
    public static IntPtr GetHandle(S7Server server)
    {
        return GetServerField(server);
    }

    /// <summary>显式销毁服务器，不依赖终结器。</summary>
    public static void Destroy(S7Server server)
    {
        ref IntPtr handle = ref GetServerField(server);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = SrvDestroy(ref handle);
        handle = IntPtr.Zero;
    }

    /// <summary>获取原生错误文本。</summary>
    public static string ErrorText(S7Server server, int error)
    {
        return server.ErrorText(error);
    }
}