using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace S7.NetCore;

/// <summary>
/// 将组件诊断日志适配到 <see cref="Microsoft.Extensions.Logging.ILogger"/>。
/// 渲染为单行条件拼接消息：有则拼接、无则省略；读写值位于消息主体，协议帧以报文为主体。
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

        _logger.Log(mapped, exception, "{Message}", BuildMessage(in entry));
    }

    private static string BuildMessage(in S7LogEntry entry)
    {
        var builder = new StringBuilder(160);
        builder.Append("S7 ").Append(entry.Operation);
        builder.Append(" stage=").Append(entry.Stage);

        if (entry.ProtocolTrace is { } protocolTrace)
        {
            if (entry.ProtocolTraceDirection != S7ProtocolTraceDirection.None)
            {
                builder.Append(" trace=")
                    .Append(entry.ProtocolTraceDirection == S7ProtocolTraceDirection.Tx ? "TX" : "RX");
            }

            if (entry.ProtocolTraceBytes is { } frameBytes)
            {
                builder.Append(" frameBytes=").Append(frameBytes.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(' ').Append(protocolTrace);
            return builder.ToString();
        }

        AppendAddress(builder, in entry);

        if (entry.DataType is { } dataType)
        {
            builder.Append(" type=").Append(dataType);
        }

        if (entry.StructTypeName is { } structType)
        {
            builder.Append(" struct=").Append(structType);
        }

        if (entry.MappedFieldCount is { } mappedFieldCount)
        {
            builder.Append(" fields=").Append(mappedFieldCount.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.BytesOccupied is { } bytesOccupied)
        {
            builder.Append(" occupied=").Append(bytesOccupied.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.Capacity is { } capacity)
        {
            builder.Append(" capacity=").Append(capacity.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.Length is { } length)
        {
            builder.Append(" len=").Append(length.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.ChunkIndex is { } chunkIndex && entry.ChunkCount is { } chunkCount)
        {
            builder.Append(" chunk=")
                .Append(chunkIndex.ToString(CultureInfo.InvariantCulture))
                .Append('/')
                .Append(chunkCount.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.ItemCount is { } itemCount)
        {
            builder.Append(" items=").Append(itemCount.ToString(CultureInfo.InvariantCulture));
            if (entry.SucceededItemCount is { } succeededItemCount)
            {
                builder.Append(" ok=").Append(succeededItemCount.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.FailedItemCount is { } failedItemCount)
            {
                builder.Append(" failed=").Append(failedItemCount.ToString(CultureInfo.InvariantCulture));
            }

            if (entry.TotalDataBytes is { } totalDataBytes)
            {
                builder.Append(" bytes=").Append(totalDataBytes.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (entry.ItemIndex is { } itemIndex)
        {
            builder.Append(" item=").Append(itemIndex.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.RequiredBytes is { } requiredBytes)
        {
            builder.Append(" required=").Append(requiredBytes.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.PduLimit is { } pduLimit)
        {
            builder.Append(" pduLimit=").Append(pduLimit.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.ItemCountLimit is { } itemCountLimit)
        {
            builder.Append(" itemLimit=").Append(itemCountLimit.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.Attempt is { } attempt)
        {
            builder.Append(" attempt=").Append(attempt.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.ReconnectDelay is { } reconnectDelay)
        {
            builder.Append(" delay=").Append(FormatElapsed(reconnectDelay));
        }

        if (entry.DisconnectedDuration is { } disconnectedDuration)
        {
            builder.Append(" disconnected=").Append(FormatElapsed(disconnectedDuration));
        }

        if (entry.NegotiatedPduSize is { } negotiatedPduSize)
        {
            builder.Append(" pdu=").Append(negotiatedPduSize.ToString(CultureInfo.InvariantCulture));
        }

        if (entry.ValueText is { } valueText)
        {
            builder.Append(" value=").Append(valueText.Replace('\r', ' ').Replace('\n', ' '));
            if (entry.ValueState == S7ValueState.NotSent)
            {
                builder.Append("(未发送)");
            }
            else if (entry.ValueState == S7ValueState.Uncertain)
            {
                builder.Append("(未确认)");
            }
        }

        builder.Append(" result=").Append(entry.Succeeded ? "OK" : "FAIL");

        if (entry.ErrorCode is { } errorCode)
        {
            builder.Append(" error=").Append(errorCode);
        }

        if (entry.PlcReturnCode is { } plcReturnCode)
        {
            builder.Append(" plcReturn=0x")
                .Append(plcReturnCode.ToString("X2", CultureInfo.InvariantCulture));
        }

        builder.Append(" elapsed=").Append(FormatElapsed(entry.Elapsed));
        return builder.ToString();
    }

    private static void AppendAddress(StringBuilder builder, in S7LogEntry entry)
    {
        if (entry.AddressExpression is { Length: > 0 } expression)
        {
            builder.Append(" addr=").Append(expression);
            return;
        }

        if (entry.Area is not { } area)
        {
            return;
        }

        if (entry.DbNumber is { } dbNumber && entry.StartByte is { } startByte)
        {
            builder.Append(" addr=")
                .Append(Internal.S7LogAddressFormatter.Format(
                    area,
                    dbNumber,
                    startByte,
                    entry.Length ?? 0,
                    entry.DataType));
            return;
        }

        builder.Append(" area=").Append(area);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
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