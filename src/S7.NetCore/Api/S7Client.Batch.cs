using System.Diagnostics;
using S7.NetCore.Internal;

namespace S7.NetCore;

public sealed partial class S7Client
{
    /// <summary>批量读取原始字节。一次调用只使用一个请求帧与一个响应帧。</summary>
    /// <param name="items">批量读条目，1～255 条。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐条结果与失败明细；条目级失败默认不抛异常。</returns>
    public async Task<S7BatchReadResult> ReadBatchAsync(
        IReadOnlyList<S7BatchReadItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            List<BatchWireItem> wires = BatchRequestValidator.ValidateRead(items);
            ValidateReadCapacity(wires);
            BatchReadRawItem[] raw = await _session.ReadBatchAsync(wires, cancellationToken).ConfigureAwait(false);
            S7BatchReadResult result = BuildReadResult(raw, items.Count);
            int failures = result.Failures.Count;
            LogBatchValues(
                "Read",
                S7OperationStage.Read,
                items.Count,
                items.Count - failures,
                failures,
                SumReadBytes(items),
                Stopwatch.GetElapsedTime(startTimestamp),
                BuildReadValueEntries(items, result, failures == 0 ? S7LogLevel.Information : S7LogLevel.Warning),
                S7ValueState.Confirmed);
            return result;
        }
        catch (S7ClientException exception)
        {
            LogBatchValidationFailure("Read", items.Count, exception);
            throw;
        }
    }

    /// <summary>批量写入原始字节。一次调用只使用一个请求帧与一个响应帧。</summary>
    /// <param name="items">批量写条目，1～255 条。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐条结果与失败明细；条目级失败默认不抛异常。</returns>
    public async Task<S7BatchWriteResult> WriteBatchAsync(
        IReadOnlyList<S7BatchWriteItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            List<BatchWireItem> wires = BatchRequestValidator.ValidateWrite(items);
            ValidateWriteCapacity(wires);
            BatchWriteRawItem[] raw = await _session.WriteBatchAsync(wires, cancellationToken).ConfigureAwait(false);
            S7BatchWriteResult result = BuildWriteResult(raw, wires);
            int failures = result.Failures.Count;
            LogBatchValues(
                "Write",
                S7OperationStage.Write,
                items.Count,
                items.Count - failures,
                failures,
                SumWireBytes(wires),
                Stopwatch.GetElapsedTime(startTimestamp),
                BuildWriteValueEntries(items, result, failures == 0 ? S7LogLevel.Information : S7LogLevel.Warning),
                failures == 0 ? S7ValueState.Confirmed : S7ValueState.Uncertain);
            return result;
        }
        catch (S7ClientException exception)
        {
            LogBatchValidationFailure("Write", items.Count, exception);
            throw;
        }
    }

    /// <summary>批量类型化读取。条目在发送前归一为一次字节批量读。</summary>
    /// <param name="items">类型化读条目，1～255 条。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐条类型化结果与失败明细。</returns>
    public async Task<S7TypedBatchReadResult> ReadTypedBatchAsync(
        IReadOnlyList<S7TypedReadBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            List<BatchTypedReadPlan> plans = BatchRequestValidator.ValidateTypedRead(items);
            BatchWireItem[] wires = plans.Select(static plan => plan.Wire).ToArray();
            ValidateReadCapacity(wires);
            BatchReadRawItem[] raw = await _session.ReadBatchAsync(wires, cancellationToken).ConfigureAwait(false);
            S7BatchReadResult byteResult = BuildReadResult(raw, plans.Count);
            S7TypedBatchReadResult result = BuildTypedReadResult(plans, raw, byteResult);
            int failures = result.Failures.Count;
            LogBatchValues(
                "Read",
                S7OperationStage.Read,
                plans.Count,
                plans.Count - failures,
                failures,
                SumTypedReadBytes(plans),
                Stopwatch.GetElapsedTime(startTimestamp),
                BuildTypedReadValueEntries(result, failures == 0 ? S7LogLevel.Information : S7LogLevel.Warning),
                S7ValueState.Confirmed);
            return result;
        }
        catch (S7ClientException exception)
        {
            LogBatchValidationFailure("Read", items.Count, exception);
            throw;
        }
    }

    /// <summary>批量类型化写入。条目在发送前全量校验、编码并归一为一次字节批量写。</summary>
    /// <param name="items">类型化写条目，1～255 条。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐条类型化结果与失败明细。</returns>
    public async Task<S7TypedBatchWriteResult> WriteTypedBatchAsync(
        IReadOnlyList<S7TypedWriteBatchItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            List<BatchTypedWritePlan> plans = BatchRequestValidator.ValidateTypedWrite(items);
            BatchWireItem[] wires = plans.Select(static plan => plan.Wire).ToArray();
            ValidateWriteCapacity(wires);
            BatchWriteRawItem[] raw = await _session.WriteBatchAsync(wires, cancellationToken).ConfigureAwait(false);
            S7BatchWriteResult byteResult = BuildWriteResult(raw, wires);
            S7TypedBatchWriteResult result = BuildTypedWriteResult(plans, raw, byteResult);
            int failures = result.Failures.Count;
            LogBatchValues(
                "Write",
                S7OperationStage.Write,
                plans.Count,
                plans.Count - failures,
                failures,
                SumTypedWriteBytes(plans),
                Stopwatch.GetElapsedTime(startTimestamp),
                BuildTypedWriteValueEntries(items, result, failures == 0 ? S7LogLevel.Information : S7LogLevel.Warning),
                failures == 0 ? S7ValueState.Confirmed : S7ValueState.Uncertain);
            return result;
        }
        catch (S7ClientException exception)
        {
            LogBatchValidationFailure("Write", items.Count, exception);
            throw;
        }
    }

    private void ValidateReadCapacity(IReadOnlyList<BatchWireItem> wires)
    {
        if (NegotiatedPduSize > 0)
        {
            BatchCapacityCalculator.ValidateRead(wires, NegotiatedPduSize);
        }
    }

    private void ValidateWriteCapacity(IReadOnlyList<BatchWireItem> wires)
    {
        if (NegotiatedPduSize > 0)
        {
            BatchCapacityCalculator.ValidateWrite(wires, NegotiatedPduSize);
        }
    }

    private static S7BatchReadResult BuildReadResult(BatchReadRawItem[] raw, int itemCount)
    {
        var results = new S7BatchReadItemResult[itemCount];
        var failures = new List<S7BatchItemFailure>();
        for (int i = 0; i < itemCount; i++)
        {
            BatchReadRawItem item = raw[i];
            results[i] = new S7BatchReadItemResult(
                i,
                item.Succeeded,
                item.Data,
                item.ErrorCode,
                item.PlcReturnCode);
            if (!item.Succeeded)
            {
                failures.Add(new S7BatchItemFailure(
                    i,
                    item.ErrorCode ?? S7ErrorCode.PlcUnknownReturnCode,
                    item.PlcReturnCode));
            }
        }

        return new S7BatchReadResult(results, failures);
    }

    private static S7BatchWriteResult BuildWriteResult(BatchWriteRawItem[] raw, IReadOnlyList<BatchWireItem> wires)
    {
        var results = new S7BatchWriteItemResult[wires.Count];
        var failures = new List<S7BatchItemFailure>();
        int bytesWritten = 0;
        for (int i = 0; i < wires.Count; i++)
        {
            byte returnCode = raw[i].ReturnCode;
            bool succeeded = returnCode == ProtocolConstants.SuccessReturnCode;
            S7ErrorCode? errorCode = null;
            if (!succeeded)
            {
                (S7ErrorCode code, _) = PlcReturnCodeMapper.Map(returnCode);
                errorCode = code;
                failures.Add(new S7BatchItemFailure(i, code, returnCode));
            }
            else
            {
                bytesWritten += wires[i].Length;
            }

            results[i] = new S7BatchWriteItemResult(
                i,
                succeeded,
                succeeded ? wires[i].Length : 0,
                errorCode,
                succeeded ? null : returnCode);
        }

        return new S7BatchWriteResult(results, failures, bytesWritten);
    }

    private static S7TypedBatchReadResult BuildTypedReadResult(
        IReadOnlyList<BatchTypedReadPlan> plans,
        BatchReadRawItem[] raw,
        S7BatchReadResult byteResult)
    {
        var results = new S7TypedReadBatchItemResult[plans.Count];
        var failures = new List<S7BatchItemFailure>();

        for (int i = 0; i < plans.Count; i++)
        {
            BatchTypedReadPlan plan = plans[i];
            BatchReadRawItem rawItem = raw[i];
            bool succeeded = rawItem.Succeeded;
            S7Value value = default;
            S7ErrorCode? errorCode = rawItem.ErrorCode;
            byte? plcReturnCode = rawItem.PlcReturnCode;

            if (succeeded)
            {
                try
                {
                    if (plan.DataType == S7DataType.Bool)
                    {
                        bool bit = (rawItem.Data[0] & (1 << plan.Address.BitNumber!.Value)) != 0;
                        value = S7Value.FromBool(bit);
                    }
                    else
                    {
                        value = S7ValueCodec.Decode(plan.DataType, rawItem.Data, plan.EffectiveCapacity);
                    }
                }
                catch (S7ClientException exception)
                {
                    succeeded = false;
                    errorCode = exception.ErrorCode;
                    value = default;
                }
            }

            if (!succeeded)
            {
                failures.Add(new S7BatchItemFailure(
                    i,
                    errorCode ?? S7ErrorCode.PlcUnknownReturnCode,
                    plcReturnCode));
            }

            results[i] = new S7TypedReadBatchItemResult(
                i,
                succeeded,
                value,
                plan.BytesOccupied,
                plan.Address,
                plan.DataType,
                plan.EffectiveCapacity,
                errorCode,
                plcReturnCode);
        }

        return new S7TypedBatchReadResult(results, failures, byteResult);
    }

    private static S7TypedBatchWriteResult BuildTypedWriteResult(
        IReadOnlyList<BatchTypedWritePlan> plans,
        BatchWriteRawItem[] raw,
        S7BatchWriteResult byteResult)
    {
        var results = new S7TypedWriteBatchItemResult[plans.Count];
        for (int i = 0; i < plans.Count; i++)
        {
            byte returnCode = raw[i].ReturnCode;
            bool succeeded = returnCode == ProtocolConstants.SuccessReturnCode;
            S7ErrorCode? errorCode = null;
            if (!succeeded)
            {
                (S7ErrorCode code, _) = PlcReturnCodeMapper.Map(returnCode);
                errorCode = code;
            }

            results[i] = new S7TypedWriteBatchItemResult(
                i,
                succeeded,
                succeeded ? plans[i].Wire.Length : 0,
                plans[i].Address,
                plans[i].DataType,
                plans[i].EffectiveCapacity,
                errorCode,
                succeeded ? null : returnCode);
        }

        return new S7TypedBatchWriteResult(results, byteResult);
    }

    private void LogBatchValidationFailure(string operation, int itemCount, S7ClientException exception)
    {
        var request = new FailureLogRequest
        {
            Level = S7LogLevel.Warning,
            Operation = operation,
            Stage = exception.Stage,
            ItemCount = itemCount,
            FailedItemCount = itemCount,
            SucceededItemCount = 0,
            ErrorCode = exception.ErrorCode,
            Cause = exception,
            ValueTraceEnabled = _options.EnableValueTrace,
            ValueState = S7ValueState.NotSent
        };

        _session.PostFailureLog(in request);
    }

    private void LogBatchValues(
        string operation,
        S7OperationStage stage,
        int itemCount,
        int succeededItemCount,
        int failedItemCount,
        int totalDataBytes,
        TimeSpan elapsed,
        S7BatchValueEntry[]? entries,
        S7ValueState state,
        S7ErrorCode? errorCode = null)
    {
        S7LogLevel level = failedItemCount == 0 ? S7LogLevel.Information : S7LogLevel.Warning;
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        S7ValueText valueList = entries is { Length: > 0 }
            ? S7ValueFormatter.FormatBatchItems(entries)
            : S7ValueText.None;
        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            ItemCount = itemCount,
            SucceededItemCount = succeededItemCount,
            FailedItemCount = failedItemCount,
            TotalDataBytes = totalDataBytes,
            Elapsed = elapsed,
            Succeeded = failedItemCount == 0,
            ErrorCode = errorCode,
            ValueText = valueList.Text,
            ValueFormat = valueList.Format,
            ValueState = valueList.Text is null ? S7ValueState.None : state,
            ValueTruncated = valueList.Truncated,
            ValueItemCount = valueList.ItemCount
        };

        _logger.Log(level, in entry);
    }

    private S7BatchValueEntry[]? BuildReadValueEntries(
        IReadOnlyList<S7BatchReadItem> items,
        S7BatchReadResult result,
        S7LogLevel level)
    {
        if (!S7LogGate.ShouldWriteValue(_logger, level, _options.EnableValueTrace))
        {
            return null;
        }

        var entries = new S7BatchValueEntry[result.Items.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            S7BatchReadItem request = items[i];
            S7BatchReadItemResult item = result.Items[i];
            string address = S7LogAddressFormatter.Format(
                request.Area,
                request.DbNumber,
                request.StartByte,
                request.Length,
                dataType: null);
            string? valueText = item.Succeeded
                ? S7ValueFormatter.FormatHex(item.Data.Span).Text
                : null;
            entries[i] = new S7BatchValueEntry(i, address, valueText, item.ErrorCode);
        }

        return entries;
    }

    private S7BatchValueEntry[]? BuildWriteValueEntries(
        IReadOnlyList<S7BatchWriteItem> items,
        S7BatchWriteResult result,
        S7LogLevel level)
    {
        if (!S7LogGate.ShouldWriteValue(_logger, level, _options.EnableValueTrace))
        {
            return null;
        }

        var entries = new S7BatchValueEntry[result.Items.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            S7BatchWriteItem request = items[i];
            S7BatchWriteItemResult item = result.Items[i];
            string address = S7LogAddressFormatter.Format(
                request.Area,
                request.DbNumber,
                request.StartByte,
                request.Data.Length,
                dataType: null);
            string? valueText = item.Succeeded
                ? S7ValueFormatter.FormatHex(request.Data.Span).Text
                : null;
            entries[i] = new S7BatchValueEntry(i, address, valueText, item.ErrorCode);
        }

        return entries;
    }

    private S7BatchValueEntry[]? BuildTypedReadValueEntries(
        S7TypedBatchReadResult result,
        S7LogLevel level)
    {
        if (!S7LogGate.ShouldWriteValue(_logger, level, _options.EnableValueTrace))
        {
            return null;
        }

        var entries = new S7BatchValueEntry[result.Items.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            S7TypedReadBatchItemResult item = result.Items[i];
            string address = ResolveAddressText(item.Address, item.BytesOccupied, item.DataType);
            string? valueText = item.Succeeded
                ? S7ValueFormatter.FormatTyped(item.DataType, item.Value).Text
                : null;
            entries[i] = new S7BatchValueEntry(i, address, valueText, item.ErrorCode);
        }

        return entries;
    }

    private S7BatchValueEntry[]? BuildTypedWriteValueEntries(
        IReadOnlyList<S7TypedWriteBatchItem> items,
        S7TypedBatchWriteResult result,
        S7LogLevel level)
    {
        if (!S7LogGate.ShouldWriteValue(_logger, level, _options.EnableValueTrace))
        {
            return null;
        }

        var entries = new S7BatchValueEntry[result.Items.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            S7TypedWriteBatchItem request = items[i];
            S7TypedWriteBatchItemResult item = result.Items[i];
            string address = ResolveAddressText(item.Address, request.Value, item.DataType);
            string? valueText = item.Succeeded
                ? S7ValueFormatter.FormatTyped(request.DataType, request.Value).Text
                : null;
            entries[i] = new S7BatchValueEntry(i, address, valueText, item.ErrorCode);
        }

        return entries;
    }

    private static string ResolveAddressText(S7Address address, in S7Value value, S7DataType dataType)
    {
        return S7LogAddressFormatter.FormatExpression(address.Expression) is { Length: > 0 } expression
            ? expression
            : S7LogAddressFormatter.Format(address.Area, address.DbNumber, address.StartByte, 0, dataType);
    }

    private static string ResolveAddressText(S7Address address, int length, S7DataType dataType)
    {
        return S7LogAddressFormatter.FormatExpression(address.Expression) is { Length: > 0 } expression
            ? expression
            : S7LogAddressFormatter.Format(address.Area, address.DbNumber, address.StartByte, length, dataType);
    }

    private static int SumReadBytes(IReadOnlyList<S7BatchReadItem> items)
    {
        int total = 0;
        foreach (S7BatchReadItem item in items)
        {
            total += item.Length;
        }

        return total;
    }

    private static int SumWireBytes(IReadOnlyList<BatchWireItem> wires)
    {
        int total = 0;
        foreach (BatchWireItem wire in wires)
        {
            total += wire.Length;
        }

        return total;
    }

    private static int SumTypedReadBytes(IReadOnlyList<BatchTypedReadPlan> plans)
    {
        int total = 0;
        foreach (BatchTypedReadPlan plan in plans)
        {
            total += plan.BytesOccupied;
        }

        return total;
    }

    private static int SumTypedWriteBytes(IReadOnlyList<BatchTypedWritePlan> plans)
    {
        int total = 0;
        foreach (BatchTypedWritePlan plan in plans)
        {
            total += plan.BytesOccupied;
        }

        return total;
    }
}