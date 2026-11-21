namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed class VirtualPlcRegionBuffer
{
    internal const int Capacity = 65536;

    private bool _valid = true;

    internal VirtualPlcRegionBuffer()
    {
        Buffer = new byte[Capacity];

        for (int i = 0; i < Capacity; i++)
        {
            Buffer[i] = (byte)((i * 7 + 11) & 0xFF);
        }
    }

    internal byte[] Buffer { get; }

    internal bool IsValid => _valid;

    internal ReadOnlyMemory<byte> CopyRange(int startByte, int length)
    {
        if (!_valid)
        {
            throw new InvalidOperationException("服务器已停止，区域缓冲区不可访问。");
        }

        if (startByte < 0 || length < 0 || startByte + length > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(startByte), "区域范围越界。");
        }

        return Buffer.AsMemory(startByte, length).ToArray();
    }

    internal void Invalidate()
    {
        _valid = false;
    }
}