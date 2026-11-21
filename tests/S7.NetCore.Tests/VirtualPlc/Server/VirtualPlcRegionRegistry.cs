using Snap7;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed record VirtualPlcRegisteredArea(int AreaCode, int Index, string Name, int Capacity);

internal sealed class VirtualPlcRegionRegistry
{
    private readonly List<Entry> _entries = [];

    internal IReadOnlyList<VirtualPlcRegisteredArea> Areas => _entries
        .Select(entry => new VirtualPlcRegisteredArea(
            entry.AreaCode,
            entry.Index,
            entry.Name,
            entry.Size))
        .ToArray();

    internal void Register(S7Server server)
    {
        Register(server, S7Server.srvAreaPE, 0, "PE");
        Register(server, S7Server.srvAreaPA, 0, "PA");
        Register(server, S7Server.srvAreaMK, 0, "MK");
        Register(server, S7Server.srvAreaDB, 1, "DB1");
        Register(server, S7Server.srvAreaDB, 2, "DB2");
        Register(server, S7Server.srvAreaDB, 3, "DB3");
    }

    internal VirtualPlcRegionBuffer GetForArea(S7Area area, int dbNumber)
    {
        int areaCode;
        int index;

        switch (area)
        {
            case S7Area.Input:
                areaCode = S7Server.srvAreaPE;
                index = 0;
                break;
            case S7Area.Output:
                areaCode = S7Server.srvAreaPA;
                index = 0;
                break;
            case S7Area.Memory:
                areaCode = S7Server.srvAreaMK;
                index = 0;
                break;
            case S7Area.DataBlock:
                areaCode = S7Server.srvAreaDB;
                index = dbNumber;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(area), $"不支持的区域：{area}");
        }

        Entry entry = _entries.FirstOrDefault(
            item => item.AreaCode == areaCode && item.Index == index)!;

        return entry?.Buffer
            ?? throw new InvalidOperationException($"服务器未注册区域 {area}（DB {dbNumber}）。");
    }

    internal void UnregisterAll(S7Server server)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            _ = server.UnregisterArea(entry.AreaCode, entry.Index);
            entry.Buffer.Invalidate();
        }

        _entries.Clear();
    }

    private void Register(S7Server server, int areaCode, int index, string name)
    {
        var buffer = new VirtualPlcRegionBuffer();
        byte[] data = buffer.Buffer;
        int size = Math.Min(data.Length, 65535);
        int result = server.RegisterArea(areaCode, index, ref data, size);

        if (result != 0)
        {
            buffer.Invalidate();
            throw new InvalidOperationException(
                $"区域 {name} 注册失败：{server.ErrorText(result)}（0x{result:X8}）。");
        }

        _entries.Add(new Entry(areaCode, index, name, buffer, size));
    }

    private sealed record Entry(
        int AreaCode,
        int Index,
        string Name,
        VirtualPlcRegionBuffer Buffer,
        int Size);
}