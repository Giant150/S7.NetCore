namespace S7.NetCore;

/// <summary>
/// 组件对外抛出的唯一通讯异常类型。调用方通过 <see cref="Category"/> 与 <see cref="ErrorCode"/>
/// 区分失败原因，通过 <see cref="Stage"/> 定位失败阶段。
/// </summary>
public sealed class S7ClientException : Exception
{
    internal S7ClientException(
        S7ErrorCategory category,
        S7ErrorCode errorCode,
        S7OperationStage stage,
        string message,
        byte? plcReturnCode = null,
        int bytesWritten = 0,
        WrittenRange? writtenRange = null,
        bool writeOutcomeUncertain = false,
        Exception? innerException = null,
        IReadOnlyList<S7BatchItemFailure>? batchFailedItems = null,
        int succeededItemCount = 0)
        : base(message, innerException)
    {
        Category = category;
        ErrorCode = errorCode;
        Stage = stage;
        PlcReturnCode = plcReturnCode;
        BytesWritten = bytesWritten;
        WrittenRange = writtenRange;
        WriteOutcomeUncertain = writeOutcomeUncertain;
        BatchFailedItems = batchFailedItems ?? Array.Empty<S7BatchItemFailure>();
        SucceededItemCount = succeededItemCount;
    }

    /// <summary>错误类别。</summary>
    public S7ErrorCategory Category { get; }

    /// <summary>稳定的错误码。</summary>
    public S7ErrorCode ErrorCode { get; }

    /// <summary>失败发生的操作阶段。</summary>
    public S7OperationStage Stage { get; }

    /// <summary>PLC 原始返回码；仅 <see cref="S7ErrorCategory.PlcReturn"/> 类别存在。</summary>
    public byte? PlcReturnCode { get; }

    /// <summary>写入操作中已确认成功写入的字节数；非写入失败时为 0。</summary>
    public int BytesWritten { get; }

    /// <summary>写入操作中已确认成功写入的区间；无已写入数据时为 <see langword="null"/>。</summary>
    public WrittenRange? WrittenRange { get; }

    /// <summary>写入结果是否不确定（例如写超时后 PLC 可能已执行写入）。</summary>
    public bool WriteOutcomeUncertain { get; }

    /// <summary>批量操作中失败条目的明细；非批量异常为空集合。</summary>
    public IReadOnlyList<S7BatchItemFailure> BatchFailedItems { get; }

    /// <summary>批量操作中成功条目数；非批量异常为 0。</summary>
    public int SucceededItemCount { get; }

    /// <summary>
    /// 生成追加上下文后缀的新异常，逐项保留类别、错误码、阶段、PLC 返回码与写入进度。
    /// </summary>
    /// <param name="suffix">追加到消息末尾的上下文。</param>
    /// <returns>新的异常实例。</returns>
    internal S7ClientException WithContext(string suffix)
    {
        return new S7ClientException(
            Category,
            ErrorCode,
            Stage,
            $"{Message} {suffix}",
            PlcReturnCode,
            BytesWritten,
            WrittenRange,
            WriteOutcomeUncertain,
            InnerException,
            BatchFailedItems,
            SucceededItemCount);
    }
}