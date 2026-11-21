using Microsoft.Extensions.Logging;

namespace S7.NetCore;

/// <summary>
/// 将组件诊断日志适配到 <see cref="Microsoft.Extensions.Logging.ILogger"/>。
/// </summary>
public sealed class S7LoggerAdapter : IS7Logger
{
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化适配器。
    /// </summary>
    /// <param name="logger">Microsoft.Extensions.Logging 日志实现。</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> 为 <see langword="null"/>。</exception>
    public S7LoggerAdapter(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled(S7LogLevel level)
    {
        return _logger.IsEnabled(Map(level));
    }

    /// <inheritdoc />
    public void Log(S7LogLevel level, in S7LogEntry entry, Exception? exception = null)
    {
        LogLevel mapped = Map(level);
        if (!_logger.IsEnabled(mapped))
        {
            return;
        }

        _logger.Log(
            mapped,
            exception,
            "S7 {Operation} stage={Stage} area={Area} db={DbNumber} start={StartByte} length={Length} chunk={ChunkIndex}/{ChunkCount} elapsed={Elapsed} succeeded={Succeeded} error={ErrorCode} plcReturn={PlcReturnCode} pdu={NegotiatedPduSize}",
            entry.Operation,
            entry.Stage,
            entry.Area,
            entry.DbNumber,
            entry.StartByte,
            entry.Length,
            entry.ChunkIndex,
            entry.ChunkCount,
            entry.Elapsed,
            entry.Succeeded,
            entry.ErrorCode,
            entry.PlcReturnCode,
            entry.NegotiatedPduSize);
    }

    private static LogLevel Map(S7LogLevel level)
    {
        return level switch
        {
            S7LogLevel.Trace => LogLevel.Trace,
            S7LogLevel.Debug => LogLevel.Debug,
            S7LogLevel.Information => LogLevel.Information,
            S7LogLevel.Warning => LogLevel.Warning,
            S7LogLevel.Error => LogLevel.Error,
            _ => LogLevel.None
        };
    }
}