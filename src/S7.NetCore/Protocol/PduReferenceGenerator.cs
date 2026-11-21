namespace S7.NetCore.Internal;

/// <summary>
/// PDU Reference 生成器。0 至 65535 循环递增，线程安全。
/// </summary>
internal sealed class PduReferenceGenerator
{
    private int _value;

    /// <summary>
    /// 获取下一个 PDU Reference。
    /// </summary>
    /// <returns>下一个引用值。</returns>
    internal ushort Next()
    {
        return unchecked((ushort)Interlocked.Increment(ref _value));
    }
}