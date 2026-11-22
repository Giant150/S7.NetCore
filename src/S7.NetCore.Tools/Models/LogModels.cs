namespace S7.NetCore.Tools.Models;

public enum LogKind
{
    Operation,
    BlockedIntent,
    Diagnostic
}

public enum OperationCategory
{
    Connect,
    Disconnect,
    ByteRead,
    ByteWrite,
    TypedRead,
    TypedWrite,
    Reconnect,
    Diagnostic,
    StructRead,
    StructWrite,
    Polling
}

public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    LogKind Kind,
    S7LogLevel Level,
    OperationCategory Category,
    S7OperationStage Stage,
    bool Succeeded,
    TimeSpan Elapsed,
    S7ErrorCategory? ErrorCategory,
    S7ErrorCode? ErrorCode,
    byte? PlcReturnCode,
    int? RecoveryCount,
    bool Retried,
    bool ReplayAttempted,
    string Summary);

public sealed record SessionTimelineEntry(
    DateTimeOffset Timestamp,
    S7SessionState PreviousState,
    S7SessionState CurrentState,
    string Reason);