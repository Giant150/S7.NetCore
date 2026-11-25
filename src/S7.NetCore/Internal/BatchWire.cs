namespace S7.NetCore.Internal;

/// <summary>批量协议条目（字节视图或类型化归一后的中间形态）。</summary>
internal readonly record struct BatchWireItem(
    S7Area Area,
    int DbNumber,
    int StartByte,
    ReadOnlyMemory<byte> Data,
    bool IsBit = false,
    int BitNumber = 0)
{
    internal int Length => Data.Length;
}

/// <summary>批量读原始条目结果。</summary>
internal readonly record struct BatchReadRawItem(
    bool Succeeded,
    byte[] Data,
    S7ErrorCode? ErrorCode,
    byte? PlcReturnCode);

/// <summary>批量写原始条目结果。</summary>
internal readonly record struct BatchWriteRawItem(byte ReturnCode);