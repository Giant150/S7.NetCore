namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 按绝对偏移生成可复现的已知模式：data[j] = ((start + j) × 7 + 11) &amp; 0xFF。
/// </summary>
internal static class KnownPattern
{
    internal static byte[] Create(int absoluteStartByte, int length)
    {
        var data = new byte[length];

        for (int j = 0; j < length; j++)
        {
            data[j] = (byte)(((absoluteStartByte + j) * 7 + 11) & 0xFF);
        }

        return data;
    }
}