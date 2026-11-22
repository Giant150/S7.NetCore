using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Services;

public sealed class ToolLogSink : IS7Logger
{
    private readonly OperationLogStore _store;
    private volatile bool _diagnosticsEnabled;

    public ToolLogSink(OperationLogStore store)
    {
        _store = store;
    }

    public bool IsDiagnosticsEnabled
    {
        get => _diagnosticsEnabled;
        set => _diagnosticsEnabled = value;
    }

    public bool IsEnabled(S7LogLevel level)
    {
        return _diagnosticsEnabled && level >= S7LogLevel.Debug;
    }

    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        try
        {
            string chunk = entry.ChunkIndex is null
                ? string.Empty
                : $" 分包 {entry.ChunkIndex}/{entry.ChunkCount}";
            string summary = $"{entry.Operation} · {Texts.Describe(entry.Stage)}{chunk}"
                + (entry.ErrorCode is null ? string.Empty : $" · {entry.ErrorCode}")
                + (exception is null ? string.Empty : $" · {exception.Message}");

            _store.Append(new OperationLogEntry(
                entry.Timestamp.ToLocalTime(),
                LogKind.Diagnostic,
                level,
                OperationCategory.Diagnostic,
                entry.Stage,
                entry.Succeeded,
                entry.Elapsed,
                null,
                entry.ErrorCode,
                entry.PlcReturnCode,
                null,
                false,
                false,
                summary));
        }
        catch
        {
            // 诊断日志不得影响主流程。
        }
    }
}