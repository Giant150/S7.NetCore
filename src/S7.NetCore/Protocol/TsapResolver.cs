namespace S7.NetCore.Internal;

/// <summary>
/// TSAP 推导。调用方显式提供时逐项覆盖，不再推导。
/// </summary>
internal static class TsapResolver
{
    /// <summary>
    /// 解析本地与远端 TSAP。
    /// </summary>
    /// <param name="options">连接参数。</param>
    /// <returns>本地与远端 TSAP。</returns>
    internal static (Tsap Local, Tsap Remote) Resolve(S7ConnectionOptions options)
    {
        (Tsap local, Tsap remote) = GetDefaultPair(options.CpuType, options.Rack, options.Slot);
        return (options.LocalTsap ?? local, options.RemoteTsap ?? remote);
    }

    private static (Tsap Local, Tsap Remote) GetDefaultPair(S7CpuType cpuType, int rack, int slot)
    {
        return cpuType switch
        {
            S7CpuType.S7200 => (new Tsap(0x10, 0x00), new Tsap(0x10, 0x01)),
            S7CpuType.Logo0BA8 => (new Tsap(0x01, 0x00), new Tsap(0x01, 0x02)),
            _ => (new Tsap(0x01, 0x00), new Tsap(0x03, (byte)((rack << 5) | slot)))
        };
    }
}