namespace S7.NetCore;

/// <summary>
/// 诊断日志级别。
/// </summary>
public enum S7LogLevel
{
    /// <summary>帧级跟踪信息，仅在显式开启协议跟踪时产生。</summary>
    Trace = 0,

    /// <summary>调试信息。</summary>
    Debug = 1,

    /// <summary>常规操作信息。</summary>
    Information = 2,

    /// <summary>告警信息。</summary>
    Warning = 3,

    /// <summary>错误信息。</summary>
    Error = 4
}

/// <summary>
/// 结构化日志条目。仅承载操作元数据，不包含读写数据内容。
/// </summary>
public readonly record struct S7LogEntry
{
    /// <summary>UTC 时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>操作类型（Connect / Disconnect / Read / Write）。</summary>
    public required string Operation { get; init; }

    /// <summary>操作阶段。</summary>
    public S7OperationStage Stage { get; init; }

    /// <summary>存储区域；会话类操作时为 <see langword="null"/>。</summary>
    public S7Area? Area { get; init; }

    /// <summary>数据块号；非数据块区或会话类操作时为 <see langword="null"/>。</summary>
    public int? DbNumber { get; init; }

    /// <summary>起始字节地址；会话类操作时为 <see langword="null"/>。</summary>
    public int? StartByte { get; init; }

    /// <summary>操作长度（字节）；会话类操作时为 <see langword="null"/>。</summary>
    public int? Length { get; init; }

    /// <summary>当前分包序号（从 1 开始）；非分包阶段时为 <see langword="null"/>。</summary>
    public int? ChunkIndex { get; init; }

    /// <summary>分包总数；非分包阶段时为 <see langword="null"/>。</summary>
    public int? ChunkCount { get; init; }

    /// <summary>操作耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>操作是否成功。</summary>
    public bool Succeeded { get; init; }

    /// <summary>失败时的错误码。</summary>
    public S7ErrorCode? ErrorCode { get; init; }

    /// <summary>PLC 原始返回码；非 PLC 返回错误时为 <see langword="null"/>。</summary>
    public byte? PlcReturnCode { get; init; }

    /// <summary>协商得到的 PDU 大小；仅连接成功日志使用。</summary>
    public int? NegotiatedPduSize { get; init; }

    /// <summary>帧级十六进制跟踪内容；仅在显式开启协议跟踪时填充。</summary>
    public string? ProtocolTrace { get; init; }

    /// <summary>裁剪后的地址表达式；仅类型化读写条目填充。</summary>
    public string? AddressExpression { get; init; }

    /// <summary>目标数据类型；仅类型化读写条目填充。</summary>
    public S7DataType? DataType { get; init; }

    /// <summary>类型占用字节数；仅类型化读写条目填充，Bool 按 1 字节载体口径。</summary>
    public int? BytesOccupied { get; init; }

    /// <summary>
    /// 字符串类容量：String/S7String 为 ASCII 字节数，S7WString 为 UTF-16 码元数；
    /// 非字符串类型与非类型化条目为 <see langword="null"/>。
    /// </summary>
    public int? Capacity { get; init; }

    /// <summary>当前重连尝试序号；非重连条目为 <see langword="null"/>。</summary>
    public int? Attempt { get; init; }

    /// <summary>最大重连尝试次数；0 表示不限，非重连条目为 <see langword="null"/>。</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>本次重连尝试的退避时长；非重连条目为 <see langword="null"/>。</summary>
    public TimeSpan? ReconnectDelay { get; init; }

    /// <summary>从断线到重连成功或耗尽的持续时长；非重连条目为 <see langword="null"/>。</summary>
    public TimeSpan? DisconnectedDuration { get; init; }

    /// <summary>结构体类型名；仅结构体读写条目填充。</summary>
    public string? StructTypeName { get; init; }

    /// <summary>结构体映射字段数；仅结构体读写条目填充。</summary>
    public int? MappedFieldCount { get; init; }
}

/// <summary>
/// 诊断日志抽象。实现必须允许多线程并发调用，且不得抛出影响主流程的异常。
/// </summary>
public interface IS7Logger
{
    /// <summary>
    /// 判断指定级别是否启用。
    /// </summary>
    /// <param name="level">待判断的日志级别。</param>
    /// <returns>启用返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    bool IsEnabled(S7LogLevel level);

    /// <summary>
    /// 写入一条结构化日志。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="entry">日志条目，仅包含元数据。</param>
    /// <param name="exception">关联异常（如有）。</param>
    void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null);
}

/// <summary>
/// 零分配空日志实现。未注入日志设施时使用。
/// </summary>
public sealed class NullS7Logger : IS7Logger
{
    private NullS7Logger()
    {
    }

    /// <summary>单例实例。</summary>
    public static NullS7Logger Instance { get; } = new();

    /// <inheritdoc />
    public bool IsEnabled(S7LogLevel level) => false;

    /// <inheritdoc />
    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
    }
}