namespace S7.NetCore.Internal;

/// <summary>
/// 惰性失败日志描述：返回路径只构造该值并投递，泵线程内完成级别判定与条目构造。
/// </summary>
internal readonly struct FailureLogRequest
{
    /// <summary>日志级别。</summary>
    internal S7LogLevel Level { get; init; }

    /// <summary>操作类型。</summary>
    internal required string Operation { get; init; }

    /// <summary>操作阶段。</summary>
    internal S7OperationStage Stage { get; init; }

    /// <summary>存储区域。</summary>
    internal S7Area? Area { get; init; }

    /// <summary>数据块号。</summary>
    internal int? DbNumber { get; init; }

    /// <summary>起始字节地址。</summary>
    internal int? StartByte { get; init; }

    /// <summary>操作长度。</summary>
    internal int? Length { get; init; }

    /// <summary>当前分包序号。</summary>
    internal int? ChunkIndex { get; init; }

    /// <summary>分包总数。</summary>
    internal int? ChunkCount { get; init; }

    /// <summary>操作总耗时。</summary>
    internal TimeSpan Elapsed { get; init; }

    /// <summary>失败时的错误码。</summary>
    internal S7ErrorCode? ErrorCode { get; init; }

    /// <summary>PLC 原始返回码。</summary>
    internal byte? PlcReturnCode { get; init; }

    /// <summary>协商得到的 PDU 大小。</summary>
    internal int? NegotiatedPduSize { get; init; }

    /// <summary>帧级十六进制跟踪内容。</summary>
    internal string? ProtocolTrace { get; init; }

    /// <summary>裁剪后的地址表达式。</summary>
    internal string? AddressExpression { get; init; }

    /// <summary>目标数据类型。</summary>
    internal S7DataType? DataType { get; init; }

    /// <summary>类型占用字节数。</summary>
    internal int? BytesOccupied { get; init; }

    /// <summary>字符串类容量。</summary>
    internal int? Capacity { get; init; }

    /// <summary>当前重连尝试序号。</summary>
    internal int? Attempt { get; init; }

    /// <summary>本次重连尝试的退避时长。</summary>
    internal TimeSpan? ReconnectDelay { get; init; }

    /// <summary>从断线到重连成功或中止的持续时长。</summary>
    internal TimeSpan? DisconnectedDuration { get; init; }

    /// <summary>结构体类型名。</summary>
    internal string? StructTypeName { get; init; }

    /// <summary>结构体映射字段数。</summary>
    internal int? MappedFieldCount { get; init; }

    /// <summary>批量条目数。</summary>
    internal int? ItemCount { get; init; }

    /// <summary>批量成功条目数。</summary>
    internal int? SucceededItemCount { get; init; }

    /// <summary>批量失败条目数。</summary>
    internal int? FailedItemCount { get; init; }

    /// <summary>批量数据总字节数。</summary>
    internal int? TotalDataBytes { get; init; }

    /// <summary>当前失败条目索引。</summary>
    internal int? ItemIndex { get; init; }

    /// <summary>超限时所需净荷字节数。</summary>
    internal int? RequiredBytes { get; init; }

    /// <summary>超限时当前 PDU。</summary>
    internal int? PduLimit { get; init; }

    /// <summary>条目数超限时的协议上限。</summary>
    internal int? ItemCountLimit { get; init; }

    /// <summary>是否允许在日志中展示值。</summary>
    internal bool ValueTraceEnabled { get; init; }

    /// <summary>写入请求的完整字节（泵线程格式化 Hex 值文本）。</summary>
    internal ReadOnlyMemory<byte> ValueBytes { get; init; }

    /// <summary>类型化写入请求值（泵线程格式化 Typed 值文本）。</summary>
    internal S7Value? TypedValue { get; init; }

    /// <summary>结构体写入请求值（装箱，泵线程格式化 JSON 值文本）。</summary>
    internal object? StructValue { get; init; }

    /// <summary>结构体布局快照。</summary>
    internal StructLayoutSnapshot? StructSnapshot { get; init; }

    /// <summary>失败时的值状态。</summary>
    internal S7ValueState ValueState { get; init; }

    /// <summary>判定原因与善后去向。</summary>
    internal string? Reason { get; init; }

    /// <summary>判定到返回的耗时。</summary>
    internal TimeSpan JudgmentElapsed { get; init; }

    /// <summary>关联异常。</summary>
    internal Exception? Cause { get; init; }

    /// <summary>
    /// 构造结构化日志条目（纯字段操作，不触达日志设施）。
    /// </summary>
    /// <param name="value">已格式化的值文本；关闭值展示或读取失败时为空。</param>
    /// <returns>日志条目。</returns>
    internal S7LogEntry ToEntry(in S7ValueText value)
    {
        S7ValueState entryValueState = !ValueTraceEnabled
            ? S7ValueState.None
            : (ValueState == S7ValueState.Confirmed && value.Text is null ? S7ValueState.None : ValueState);

        return new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = Operation,
            Stage = Stage,
            Area = Area,
            DbNumber = DbNumber,
            StartByte = StartByte,
            Length = Length,
            ChunkIndex = ChunkIndex,
            ChunkCount = ChunkCount,
            Elapsed = Elapsed,
            Succeeded = false,
            ErrorCode = ErrorCode,
            PlcReturnCode = PlcReturnCode,
            NegotiatedPduSize = NegotiatedPduSize,
            ProtocolTrace = ProtocolTrace,
            AddressExpression = AddressExpression,
            DataType = DataType,
            BytesOccupied = BytesOccupied,
            Capacity = Capacity,
            Attempt = Attempt,
            ReconnectDelay = ReconnectDelay,
            DisconnectedDuration = DisconnectedDuration,
            StructTypeName = StructTypeName,
            MappedFieldCount = MappedFieldCount,
            ItemCount = ItemCount,
            SucceededItemCount = SucceededItemCount,
            FailedItemCount = FailedItemCount,
            TotalDataBytes = TotalDataBytes,
            ItemIndex = ItemIndex,
            RequiredBytes = RequiredBytes,
            PduLimit = PduLimit,
            ItemCountLimit = ItemCountLimit,
            ValueText = value.Text,
            ValueFormat = value.Format,
            ValueState = entryValueState,
            ValueTruncated = value.Truncated,
            ValueItemCount = value.ItemCount
        };
    }
}
