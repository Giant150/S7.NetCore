namespace S7.NetCore.Internal;

internal sealed partial class S7Session
{
    internal async Task<BatchReadRawItem[]> ReadBatchAsync(
        IReadOnlyList<BatchWireItem> items,
        CancellationToken cancellationToken)
    {
        BatchReadRawItem[] raw = await ReadBatchOnceAsync(items, cancellationToken).ConfigureAwait(false);
        int failed = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (!raw[i].Succeeded)
            {
                failed++;
            }
        }

        LogBatch(
            new OperationScope(),
            "Read",
            S7OperationStage.Read,
            failed == 0,
            items.Count,
            items.Count - failed,
            failed,
            TotalBytes(items));
        return raw;
    }

    internal async Task<BatchWriteRawItem[]> WriteBatchAsync(
        IReadOnlyList<BatchWireItem> items,
        CancellationToken cancellationToken)
    {
        BatchWriteRawItem[] raw = await WriteBatchOnceAsync(items, cancellationToken).ConfigureAwait(false);
        int failed = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].ReturnCode != ProtocolConstants.SuccessReturnCode)
            {
                failed++;
            }
        }

        LogBatch(
            new OperationScope(),
            "Write",
            S7OperationStage.Write,
            failed == 0,
            items.Count,
            items.Count - failed,
            failed,
            TotalBytes(items));
        return raw;
    }

    private async Task<BatchReadRawItem[]> ReadBatchOnceAsync(
        IReadOnlyList<BatchWireItem> items,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Read, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Read,
                DescribeBatch("读取", items),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Read, cancellationToken).ConfigureAwait(false);
        BatchCapacityCalculator.ValidateRead(items, NegotiatedPduSize);

        var scope = new OperationScope();
        ITcpTransport transport = _transport!;
        using var timeout = new OperationTimeout(_options.ReadTimeout, cancellationToken, _timeProvider);
        ushort pduReference = _references.Next();
        byte[] frame = S7BatchRequestBuilder.BuildReadRequest(items, pduReference);

        try
        {
            TraceFrame("Read", S7OperationStage.Read, frame);
            await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            byte[] payload = await S7FrameReader
                .ReadS7PayloadAsync(transport, S7OperationStage.Read, timeout.Token)
                .ConfigureAwait(false);

            BatchReadRawItem[] raw = S7BatchResponseParser.ParseReadResponse(payload, pduReference, items);
            LogFailedReadItems(scope, items, raw);
            return raw;
        }
        catch (OperationCanceledException ex)
        {
            if (timeout.CallerTriggered)
            {
                bool drained = await DrainAsync(transport, pduReference, S7OperationStage.Read).ConfigureAwait(false);
                if (!drained)
                {
                    await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                }
            }

            S7ClientException mapped = MapCanceled(
                timeout,
                S7OperationStage.Read,
                S7ErrorCode.ReadTimeout,
                DescribeBatch("读取", items),
                ex);
            LogBatchFailure(scope, "Read", S7OperationStage.Read, items, mapped);
            throw mapped;
        }
        catch (TransportException ex)
        {
            await HandleFaultAsync(
                    transport,
                    S7OperationStage.Read,
                    S7ErrorCode.ConnectionReset,
                    S7ErrorCategory.Connection,
                    callerCancelled: false)
                .ConfigureAwait(false);

            var mapped = new S7ClientException(
                S7ErrorCategory.Connection,
                S7ErrorCode.ConnectionReset,
                S7OperationStage.Read,
                $"批量读取过程中连接中断：{DescribeBatch("读取", items)}。",
                innerException: ex);
            LogBatchFailure(scope, "Read", S7OperationStage.Read, items, mapped);
            throw mapped;
        }
        catch (S7ClientException ex)
        {
            LogBatchFailure(scope, "Read", S7OperationStage.Read, items, ex);
            throw;
        }
    }

    private async Task<BatchWriteRawItem[]> WriteBatchOnceAsync(
        IReadOnlyList<BatchWireItem> items,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        using RequestGate.Lease lease = await AcquireGateAsync(
                S7OperationStage.Write,
                DescribeBatch("写入", items),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureConnectedAsync(S7OperationStage.Write, cancellationToken).ConfigureAwait(false);
        BatchCapacityCalculator.ValidateWrite(items, NegotiatedPduSize);

        var scope = new OperationScope();
        ITcpTransport transport = _transport!;
        using var timeout = new OperationTimeout(_options.WriteTimeout, cancellationToken, _timeProvider);
        ushort pduReference = _references.Next();
        byte[] frame = S7BatchRequestBuilder.BuildWriteRequest(items, pduReference);
        bool writeIssued = false;

        try
        {
            TraceFrame("Write", S7OperationStage.Write, frame);
            await transport.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            writeIssued = true;
            byte[] payload = await S7FrameReader
                .ReadS7PayloadAsync(transport, S7OperationStage.Write, timeout.Token)
                .ConfigureAwait(false);

            BatchWriteRawItem[] raw = S7BatchResponseParser.ParseWriteResponse(payload, pduReference, items.Count);
            LogFailedWriteItems(scope, items, raw);
            return raw;
        }
        catch (OperationCanceledException ex)
        {
            if (timeout.CallerTriggered)
            {
                bool drained = await DrainAsync(transport, pduReference, S7OperationStage.Write).ConfigureAwait(false);
                if (!drained)
                {
                    await CloseTransportAfterFaultAsync(transport).ConfigureAwait(false);
                }
            }

            S7ClientException mapped = MapCanceled(
                timeout,
                S7OperationStage.Write,
                S7ErrorCode.WriteTimeout,
                DescribeBatch("写入", items),
                ex,
                writeOutcomeUncertain: writeIssued);
            LogBatchFailure(scope, "Write", S7OperationStage.Write, items, mapped);
            throw mapped;
        }
        catch (TransportException ex)
        {
            await HandleFaultAsync(
                    transport,
                    S7OperationStage.Write,
                    S7ErrorCode.ConnectionReset,
                    S7ErrorCategory.Connection,
                    callerCancelled: false)
                .ConfigureAwait(false);

            var mapped = new S7ClientException(
                S7ErrorCategory.Connection,
                S7ErrorCode.ConnectionReset,
                S7OperationStage.Write,
                $"批量写入过程中连接中断：{DescribeBatch("写入", items)}。",
                writeOutcomeUncertain: writeIssued,
                innerException: ex);
            LogBatchFailure(scope, "Write", S7OperationStage.Write, items, mapped);
            throw mapped;
        }
        catch (S7ClientException ex)
        {
            LogBatchFailure(scope, "Write", S7OperationStage.Write, items, ex);
            throw;
        }
    }

    private void LogFailedReadItems(OperationScope scope, IReadOnlyList<BatchWireItem> items, BatchReadRawItem[] raw)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].Succeeded)
            {
                continue;
            }

            LogBatchItemFailure(
                scope,
                "Read",
                S7OperationStage.Read,
                i,
                items[i],
                raw[i].ErrorCode ?? S7ErrorCode.ResponseTooShort,
                raw[i].PlcReturnCode);
        }
    }

    private void LogFailedWriteItems(OperationScope scope, IReadOnlyList<BatchWireItem> items, BatchWriteRawItem[] raw)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].ReturnCode == ProtocolConstants.SuccessReturnCode)
            {
                continue;
            }

            (S7ErrorCode code, _) = PlcReturnCodeMapper.Map(raw[i].ReturnCode);
            LogBatchItemFailure(scope, "Write", S7OperationStage.Write, i, items[i], code, raw[i].ReturnCode);
        }
    }

    private void LogBatchFailure(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        IReadOnlyList<BatchWireItem> items,
        S7ClientException exception)
    {
        LogBatch(
            scope,
            operation,
            stage,
            succeeded: false,
            items.Count,
            succeededItemCount: 0,
            failedItemCount: items.Count,
            TotalBytes(items),
            exception.ErrorCode,
            exception);
    }

    private void LogBatchItemFailure(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        int index,
        BatchWireItem item,
        S7ErrorCode errorCode,
        byte? plcReturnCode)
    {
        if (!_logger.IsEnabled(S7LogLevel.Warning))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            Area = item.Area,
            DbNumber = item.DbNumber,
            StartByte = item.StartByte,
            Length = item.Length,
            ItemIndex = index,
            ErrorCode = errorCode,
            PlcReturnCode = plcReturnCode,
            Elapsed = scope.Elapsed,
            Succeeded = false
        };
        _logger.Log(S7LogLevel.Warning, in entry);
    }

    private void LogBatch(
        OperationScope scope,
        string operation,
        S7OperationStage stage,
        bool succeeded,
        int itemCount,
        int succeededItemCount,
        int failedItemCount,
        int totalDataBytes,
        S7ErrorCode? errorCode = null,
        Exception? exception = null)
    {
        S7LogLevel level = succeeded ? S7LogLevel.Information : S7LogLevel.Warning;
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            ItemCount = itemCount,
            SucceededItemCount = succeededItemCount,
            FailedItemCount = failedItemCount,
            TotalDataBytes = totalDataBytes,
            Elapsed = scope.Elapsed,
            Succeeded = succeeded,
            ErrorCode = errorCode
        };
        _logger.Log(level, in entry, exception);
    }

    private static int TotalBytes(IReadOnlyList<BatchWireItem> items)
    {
        int total = 0;
        foreach (BatchWireItem item in items)
        {
            total += item.Length;
        }

        return total;
    }

    private static string DescribeBatch(string operation, IReadOnlyList<BatchWireItem> items)
    {
        int total = TotalBytes(items);
        return $"批量{operation}：{items.Count} 条，合计 {total} 字节";
    }
}