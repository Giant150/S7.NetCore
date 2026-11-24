namespace S7.NetCore.Internal;

internal readonly record struct BatchTypedReadPlan(
    S7Address Address,
    S7DataType DataType,
    int Capacity,
    int BytesOccupied,
    BatchWireItem Wire);

internal readonly record struct BatchTypedWritePlan(
    S7Address Address,
    S7DataType DataType,
    int Capacity,
    int BytesOccupied,
    BatchWireItem Wire);

/// <summary>批量请求校验与归一。</summary>
internal static class BatchRequestValidator
{
    internal static List<BatchWireItem> ValidateRead(IReadOnlyList<S7BatchReadItem> items)
    {
        ValidateCount(items.Count);
        var wires = new List<BatchWireItem>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            S7BatchReadItem item = items[i];
            RequestValidator.ValidateRead(new S7ReadRequest(item.Area, item.DbNumber, item.StartByte, item.Length));
            wires.Add(new BatchWireItem(
                item.Area,
                item.DbNumber,
                item.StartByte,
                new byte[item.Length]));
        }

        return wires;
    }

    internal static List<BatchWireItem> ValidateWrite(IReadOnlyList<S7BatchWriteItem> items)
    {
        ValidateCount(items.Count);
        var wires = new List<BatchWireItem>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            S7BatchWriteItem item = items[i];
            byte[] data = item.Data.ToArray();
            RequestValidator.ValidateWrite(new S7WriteRequest(item.Area, item.DbNumber, item.StartByte, data));
            wires.Add(new BatchWireItem(item.Area, item.DbNumber, item.StartByte, data));
        }

        WriteOverlapDetector.Validate(wires);
        return wires;
    }

    internal static List<BatchTypedReadPlan> ValidateTypedRead(IReadOnlyList<S7TypedReadBatchItem> items)
    {
        ValidateCount(items.Count);
        var plans = new List<BatchTypedReadPlan>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            S7TypedReadBatchItem item = items[i];
            S7Address address = S7Address.Parse(item.Address);
            int occupied = TypedRequestValidator.ValidateRead(
                new S7TypedReadRequest(item.Address, item.DataType, item.Capacity),
                in address);
            plans.Add(new BatchTypedReadPlan(
                address,
                item.DataType,
                item.Capacity,
                occupied,
                new BatchWireItem(address.Area, address.DbNumber, address.StartByte, new byte[occupied])));
        }

        return plans;
    }

    internal static List<BatchTypedWritePlan> ValidateTypedWrite(IReadOnlyList<S7TypedWriteBatchItem> items)
    {
        ValidateCount(items.Count);
        var plans = new List<BatchTypedWritePlan>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            S7TypedWriteBatchItem item = items[i];
            S7Address address = S7Address.Parse(item.Address);
            int occupied = TypedRequestValidator.ValidateWrite(
                new S7TypedWriteRequest(item.Address, item.DataType, item.Value, item.Capacity),
                in address);

            BatchWireItem wire;
            if (item.DataType == S7DataType.Bool)
            {
                wire = new BatchWireItem(
                    address.Area,
                    address.DbNumber,
                    address.StartByte,
                    new[] { item.Value.AsBoolean() ? (byte)1 : (byte)0 },
                    IsBit: true,
                    BitNumber: address.BitNumber!.Value);
            }
            else
            {
                byte[] encoded = S7ValueCodec.Encode(item.DataType, item.Value, item.Capacity);
                wire = new BatchWireItem(address.Area, address.DbNumber, address.StartByte, encoded);
            }

            plans.Add(new BatchTypedWritePlan(address, item.DataType, item.Capacity, occupied, wire));
        }

        WriteOverlapDetector.Validate(plans.Select(static plan => plan.Wire).ToArray());
        return plans;
    }

    private static void ValidateCount(int count)
    {
        if (count == 0)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.BatchItemsEmpty,
                S7OperationStage.Validate,
                "批量条目不能为空，至少需要 1 条。");
        }

        if (count > ProtocolConstants.MaxBatchItemCount)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.BatchItemCountExceeded,
                S7OperationStage.Validate,
                $"批量条目数 {count} 超过协议计数字段上限 {ProtocolConstants.MaxBatchItemCount}。");
        }
    }
}