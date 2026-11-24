namespace S7.NetCore.Internal;

/// <summary>
/// 批量容量计算。全部使用 S7 净荷口径（不含 TPKT/COTP）。
/// </summary>
internal static class BatchCapacityCalculator
{
    internal static int Align(int length) => length + (length & 1);

    internal static long ReadRequestRequired(int itemCount)
        => ProtocolConstants.S7JobPayloadHeaderSize + ((long)ProtocolConstants.ReadItemHeaderSize * itemCount);

    internal static long ReadResponseRequired(IReadOnlyList<BatchWireItem> items)
    {
        long total = ProtocolConstants.S7AckPayloadHeaderSize;
        foreach (BatchWireItem item in items)
        {
            total += ProtocolConstants.ReadItemResultHeaderSize + Align(item.Length);
        }

        return total;
    }

    internal static long WriteRequestRequired(IReadOnlyList<BatchWireItem> items)
    {
        long total = ProtocolConstants.S7JobPayloadHeaderSize;
        foreach (BatchWireItem item in items)
        {
            total += ProtocolConstants.ReadItemHeaderSize
                + ProtocolConstants.WriteItemDataHeaderSize
                + Align(item.Length);
        }

        return total;
    }

    internal static long WriteResponseRequired(int itemCount)
        => ProtocolConstants.S7AckPayloadHeaderSize
            + ((long)ProtocolConstants.WriteItemResultCodeSize * itemCount);

    internal static void ValidateRead(IReadOnlyList<BatchWireItem> items, int pduSize)
    {
        if (pduSize <= 0)
        {
            return;
        }

        long request = ReadRequestRequired(items.Count);
        long response = ReadResponseRequired(items);
        if (request > pduSize || response > pduSize)
        {
            string side = request > pduSize && response > pduSize
                ? "请求侧与响应侧"
                : request > pduSize ? "请求侧" : "响应侧";
            throw PduExceeded(side, Math.Max(request, response), pduSize);
        }
    }

    internal static void ValidateWrite(IReadOnlyList<BatchWireItem> items, int pduSize)
    {
        if (pduSize <= 0)
        {
            return;
        }

        long request = WriteRequestRequired(items);
        if (request > pduSize)
        {
            throw PduExceeded("请求侧", request, pduSize);
        }
    }

    internal static S7ClientException PduExceeded(string side, long requiredBytes, int pduSize)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.BatchPduExceeded,
            S7OperationStage.Validate,
            $"批量{side}所需净荷 {requiredBytes} 字节，超过当前 PDU 上限 {pduSize} 字节；"
            + "请减少条目数或按 PDU 容量自行分组后多次调用。");
    }
}