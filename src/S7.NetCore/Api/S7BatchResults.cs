using System.Collections.ObjectModel;

namespace S7.NetCore;

/// <summary>批量读单条目结果。</summary>
public sealed class S7BatchReadItemResult
{
    internal S7BatchReadItemResult(int index, bool succeeded, ReadOnlyMemory<byte> data, S7ErrorCode? errorCode, byte? plcReturnCode)
    {
        Index = index;
        Succeeded = succeeded;
        Data = data;
        ErrorCode = errorCode;
        PlcReturnCode = plcReturnCode;
    }

    /// <summary>请求中的条目索引，从 0 开始。</summary>
    public int Index { get; }

    /// <summary>条目是否成功。</summary>
    public bool Succeeded { get; }

    /// <summary>成功时的数据；失败时为空。</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>失败时的稳定错误码。</summary>
    public S7ErrorCode? ErrorCode { get; }

    /// <summary>PLC 原始返回码；条目级 PLC 失败时非空。</summary>
    public byte? PlcReturnCode { get; }
}

/// <summary>批量写单条目结果。</summary>
public sealed class S7BatchWriteItemResult
{
    internal S7BatchWriteItemResult(int index, bool succeeded, int bytesWritten, S7ErrorCode? errorCode, byte? plcReturnCode)
    {
        Index = index;
        Succeeded = succeeded;
        BytesWritten = bytesWritten;
        ErrorCode = errorCode;
        PlcReturnCode = plcReturnCode;
    }

    /// <summary>请求中的条目索引，从 0 开始。</summary>
    public int Index { get; }

    /// <summary>条目是否成功。</summary>
    public bool Succeeded { get; }

    /// <summary>成功时写入的字节数；位条目为 1。</summary>
    public int BytesWritten { get; }

    /// <summary>失败时的稳定错误码。</summary>
    public S7ErrorCode? ErrorCode { get; }

    /// <summary>PLC 原始返回码；条目级 PLC 失败时非空。</summary>
    public byte? PlcReturnCode { get; }
}

/// <summary>批量类型化读单条目结果。</summary>
public sealed class S7TypedReadBatchItemResult
{
    internal S7TypedReadBatchItemResult(
        int index,
        bool succeeded,
        S7Value value,
        int bytesOccupied,
        S7Address address,
        S7DataType dataType,
        int effectiveCapacity,
        S7ErrorCode? errorCode,
        byte? plcReturnCode)
    {
        Index = index;
        Succeeded = succeeded;
        Value = value;
        BytesOccupied = bytesOccupied;
        Address = address;
        DataType = dataType;
        EffectiveCapacity = effectiveCapacity;
        ErrorCode = errorCode;
        PlcReturnCode = plcReturnCode;
    }

    /// <summary>请求中的条目索引，从 0 开始。</summary>
    public int Index { get; }

    /// <summary>条目是否成功。</summary>
    public bool Succeeded { get; }

    /// <summary>成功时的解码值；失败时为未承载值。</summary>
    public S7Value Value { get; }

    /// <summary>该类型占用字节数。</summary>
    public int BytesOccupied { get; }

    /// <summary>解析后的地址。</summary>
    public S7Address Address { get; }

    /// <summary>本条目的生效数据类型。</summary>
    public S7DataType DataType { get; }

    /// <summary>本条目的有效字符串容量；非字符串类型为 0。</summary>
    public int EffectiveCapacity { get; }

    /// <summary>失败时的稳定错误码。</summary>
    public S7ErrorCode? ErrorCode { get; }

    /// <summary>PLC 原始返回码；条目级 PLC 失败时非空。</summary>
    public byte? PlcReturnCode { get; }
}

/// <summary>批量类型化写单条目结果。</summary>
public sealed class S7TypedWriteBatchItemResult
{
    internal S7TypedWriteBatchItemResult(
        int index,
        bool succeeded,
        int bytesWritten,
        S7Address address,
        S7DataType dataType,
        int effectiveCapacity,
        S7ErrorCode? errorCode,
        byte? plcReturnCode)
    {
        Index = index;
        Succeeded = succeeded;
        BytesWritten = bytesWritten;
        Address = address;
        DataType = dataType;
        EffectiveCapacity = effectiveCapacity;
        ErrorCode = errorCode;
        PlcReturnCode = plcReturnCode;
    }

    /// <summary>请求中的条目索引，从 0 开始。</summary>
    public int Index { get; }

    /// <summary>条目是否成功。</summary>
    public bool Succeeded { get; }

    /// <summary>成功时写入的字节数；位条目为 1。</summary>
    public int BytesWritten { get; }

    /// <summary>解析后的地址。</summary>
    public S7Address Address { get; }

    /// <summary>本条目的生效数据类型。</summary>
    public S7DataType DataType { get; }

    /// <summary>本条目的有效字符串容量；非字符串类型为 0。</summary>
    public int EffectiveCapacity { get; }

    /// <summary>失败时的稳定错误码。</summary>
    public S7ErrorCode? ErrorCode { get; }

    /// <summary>PLC 原始返回码；条目级 PLC 失败时非空。</summary>
    public byte? PlcReturnCode { get; }
}

/// <summary>批量字节读取结果。</summary>
public sealed class S7BatchReadResult
{
    internal S7BatchReadResult(
        IReadOnlyList<S7BatchReadItemResult> items,
        IReadOnlyList<S7BatchItemFailure> failures,
        int recoveryCount,
        bool retried)
    {
        Items = new ReadOnlyCollection<S7BatchReadItemResult>(items as IList<S7BatchReadItemResult> ?? items.ToArray());
        Failures = new ReadOnlyCollection<S7BatchItemFailure>(failures as IList<S7BatchItemFailure> ?? failures.ToArray());
        RecoveryCount = recoveryCount;
        Retried = retried;
    }

    /// <summary>逐条结果，数量与顺序恒等于请求。</summary>
    public IReadOnlyList<S7BatchReadItemResult> Items { get; }

    /// <summary>失败条目明细，按索引升序。</summary>
    public IReadOnlyList<S7BatchItemFailure> Failures { get; }

    /// <summary>是否全部条目成功。</summary>
    public bool AllSucceeded => Failures.Count == 0;

    /// <summary>本次调用中因重连恢复的次数，取值 0 或 1。</summary>
    public int RecoveryCount { get; }

    /// <summary>是否在重连恢复后重试并成功。</summary>
    public bool Retried { get; }

    /// <summary>全部成功时直接返回；存在失败时抛 144 并携带明细。</summary>
    public void EnsureAllSucceeded()
    {
        if (AllSucceeded)
        {
            return;
        }

        throw BatchFailure.Create(this, S7OperationStage.Read, 0, null);
    }
}

/// <summary>批量字节写入结果。</summary>
public sealed class S7BatchWriteResult
{
    internal S7BatchWriteResult(
        IReadOnlyList<S7BatchWriteItemResult> items,
        IReadOnlyList<S7BatchItemFailure> failures,
        int bytesWritten,
        int recoveryCount,
        bool retried,
        bool replayAttempted,
        int replayedBytes)
    {
        Items = new ReadOnlyCollection<S7BatchWriteItemResult>(items as IList<S7BatchWriteItemResult> ?? items.ToArray());
        Failures = new ReadOnlyCollection<S7BatchItemFailure>(failures as IList<S7BatchItemFailure> ?? failures.ToArray());
        BytesWritten = bytesWritten;
        RecoveryCount = recoveryCount;
        Retried = retried;
        ReplayAttempted = replayAttempted;
        ReplayedBytes = replayedBytes;
    }

    /// <summary>逐条结果，数量与顺序恒等于请求。</summary>
    public IReadOnlyList<S7BatchWriteItemResult> Items { get; }

    /// <summary>失败条目明细，按索引升序。</summary>
    public IReadOnlyList<S7BatchItemFailure> Failures { get; }

    /// <summary>是否全部条目成功。</summary>
    public bool AllSucceeded => Failures.Count == 0;

    /// <summary>成功条目数据长度之和；位条目计 1。</summary>
    public int BytesWritten { get; }

    /// <summary>本次调用中因重连恢复的次数，取值 0 或 1。</summary>
    public int RecoveryCount { get; }

    /// <summary>是否在重连恢复后重试并成功。</summary>
    public bool Retried { get; }

    /// <summary>是否因结果不确定而启用重放。</summary>
    public bool ReplayAttempted { get; }

    /// <summary>实际重放的数据总字节数；未重放为 0。</summary>
    public int ReplayedBytes { get; }

    /// <summary>全部成功时直接返回；存在失败时抛 144 并携带明细。</summary>
    public void EnsureAllSucceeded()
    {
        if (AllSucceeded)
        {
            return;
        }

        throw BatchFailure.Create(this, S7OperationStage.Write, BytesWritten, null);
    }
}

/// <summary>批量类型化读取结果。</summary>
public sealed class S7TypedBatchReadResult
{
    internal S7TypedBatchReadResult(
        IReadOnlyList<S7TypedReadBatchItemResult> items,
        IReadOnlyList<S7BatchItemFailure> failures,
        S7BatchReadResult byteResult)
    {
        Items = new ReadOnlyCollection<S7TypedReadBatchItemResult>(items as IList<S7TypedReadBatchItemResult> ?? items.ToArray());
        Failures = new ReadOnlyCollection<S7BatchItemFailure>(failures as IList<S7BatchItemFailure> ?? failures.ToArray());
        ByteResult = byteResult;
    }

    /// <summary>逐条类型化结果。</summary>
    public IReadOnlyList<S7TypedReadBatchItemResult> Items { get; }

    /// <summary>底层字节结果。</summary>
    public S7BatchReadResult ByteResult { get; }

    /// <summary>是否全部条目成功。</summary>
    public bool AllSucceeded => Items.All(static item => item.Succeeded);

    /// <summary>失败条目明细。</summary>
    public IReadOnlyList<S7BatchItemFailure> Failures { get; }

    /// <summary>本次调用中因重连恢复的次数。</summary>
    public int RecoveryCount => ByteResult.RecoveryCount;

    /// <summary>是否在重连恢复后重试并成功。</summary>
    public bool Retried => ByteResult.Retried;

    /// <summary>全部成功时直接返回；存在失败时抛 144 并携带明细。</summary>
    public void EnsureAllSucceeded()
    {
        if (AllSucceeded)
        {
            return;
        }

        throw BatchFailure.Create(Failures, S7OperationStage.Read, 0, null, Items.Count(static item => item.Succeeded));
    }
}

/// <summary>批量类型化写入结果。</summary>
public sealed class S7TypedBatchWriteResult
{
    internal S7TypedBatchWriteResult(
        IReadOnlyList<S7TypedWriteBatchItemResult> items,
        S7BatchWriteResult byteResult)
    {
        Items = new ReadOnlyCollection<S7TypedWriteBatchItemResult>(items as IList<S7TypedWriteBatchItemResult> ?? items.ToArray());
        ByteResult = byteResult;
    }

    /// <summary>逐条类型化结果。</summary>
    public IReadOnlyList<S7TypedWriteBatchItemResult> Items { get; }

    /// <summary>底层字节结果。</summary>
    public S7BatchWriteResult ByteResult { get; }

    /// <summary>是否全部条目成功。</summary>
    public bool AllSucceeded => ByteResult.AllSucceeded;

    /// <summary>失败条目明细。</summary>
    public IReadOnlyList<S7BatchItemFailure> Failures => ByteResult.Failures;

    /// <summary>成功条目数据长度之和；位条目计 1。</summary>
    public int BytesWritten => ByteResult.BytesWritten;

    /// <summary>本次调用中因重连恢复的次数。</summary>
    public int RecoveryCount => ByteResult.RecoveryCount;

    /// <summary>是否在重连恢复后重试并成功。</summary>
    public bool Retried => ByteResult.Retried;

    /// <summary>全部成功时直接返回；存在失败时抛 144 并携带明细。</summary>
    public void EnsureAllSucceeded()
    {
        ByteResult.EnsureAllSucceeded();
    }
}

internal static class BatchFailure
{
internal static S7ClientException Create(
        IReadOnlyList<S7BatchItemFailure> failures,
        S7OperationStage stage,
        int bytesWritten,
        WrittenRange? writtenRange,
        int succeededItemCount)
    {
        string indexes = string.Join(",", failures.Select(static failure => failure.Index));
        return new S7ClientException(
            S7ErrorCategory.PlcReturn,
            S7ErrorCode.BatchItemFailed,
            stage,
            $"批量操作存在失败条目：索引 {indexes}。",
            bytesWritten: bytesWritten,
            writtenRange: writtenRange,
            batchFailedItems: failures,
            succeededItemCount: succeededItemCount);
    }

internal static S7ClientException Create(
        S7BatchReadResult result,
        S7OperationStage stage,
        int bytesWritten,
        WrittenRange? writtenRange)
    {
        return Create(
            result.Failures,
            stage,
            bytesWritten,
            writtenRange,
            result.Items.Count - result.Failures.Count);
    }

    internal static S7ClientException Create(
        S7BatchWriteResult result,
        S7OperationStage stage,
        int bytesWritten,
        WrittenRange? writtenRange)
    {
        return Create(
            result.Failures,
            stage,
            bytesWritten,
            writtenRange,
            result.Items.Count - result.Failures.Count);
    }

    private static S7ClientException Create(
        IReadOnlyList<S7BatchItemFailure> failures,
        S7OperationStage stage,
        int bytesWritten,
        WrittenRange? writtenRange)
    {
        int succeeded = 0;
        string indexes = string.Join(",", failures.Select(f => f.Index));
        return new S7ClientException(
            S7ErrorCategory.PlcReturn,
            S7ErrorCode.BatchItemFailed,
            stage,
            $"批量操作存在失败条目：索引 {indexes}。",
            bytesWritten: bytesWritten,
            writtenRange: writtenRange,
            batchFailedItems: failures,
            succeededItemCount: succeeded);
    }
}