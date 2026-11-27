using System.Threading.Channels;

namespace S7.NetCore.Internal;

/// <summary>
/// 会话内部副作用泵：把状态迁移通知与失败日志移出调用方返回路径。
/// 无界 FIFO 队列 + 单消费者，投递方只做非阻塞 TryWrite。
/// </summary>
internal sealed class SideEffectPump
{
    private readonly Channel<SideEffect> _queue;
    private readonly IS7Logger _logger;
    private readonly Action<S7SessionStateChangedEventArgs> _dispatchNotification;
    private readonly TimeProvider _timeProvider;
    private readonly Task _worker;
    private volatile bool _abandoned;

    /// <summary>
    /// 初始化副作用泵。
    /// </summary>
    /// <param name="logger">日志设施。</param>
    /// <param name="dispatchNotification">通知派发回调。</param>
    /// <param name="timeProvider">时间源，用于生成通知时间戳。</param>
    internal SideEffectPump(
        IS7Logger logger,
        Action<S7SessionStateChangedEventArgs> dispatchNotification,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _dispatchNotification = dispatchNotification;
        _timeProvider = timeProvider;
        _queue = Channel.CreateUnbounded<SideEffect>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(RunAsync);
    }

    /// <summary>
    /// 投递一条状态迁移通知。
    /// </summary>
    /// <param name="previous">迁移前状态。</param>
    /// <param name="current">迁移后状态。</param>
    /// <param name="sessionId">会话标识。</param>
    internal void PostNotification(S7SessionState previous, S7SessionState current, Guid sessionId)
    {
        _queue.Writer.TryWrite(SideEffect.CreateNotification(previous, current, sessionId));
    }

    /// <summary>
    /// 投递一条已构造好的失败日志（保留给内部既有调用点；级别判定在泵内统一执行）。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="entry">日志条目。</param>
    /// <param name="cause">关联异常。</param>
    internal void PostFailureLog(S7LogLevel level, in S7LogEntry entry, Exception? cause)
    {
        _queue.Writer.TryWrite(SideEffect.CreateFailureLog(level, entry, cause));
    }

    /// <summary>
    /// 投递一条惰性失败日志描述；返回路径不调用日志设施。
    /// </summary>
    /// <param name="request">失败日志描述。</param>
    internal void PostFailureLog(in FailureLogRequest request)
    {
        _queue.Writer.TryWrite(SideEffect.CreateFailureLog(in request));
    }

    /// <summary>
    /// 完成队列并尽力在指定时间内排空。
    /// </summary>
    /// <param name="flushTimeout">排空上限。</param>
    /// <returns>排空任务。</returns>
    internal async Task CompleteAsync(TimeSpan flushTimeout)
    {
        _queue.Writer.TryComplete();

        try
        {
            await _worker.WaitAsync(flushTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _abandoned = true;
        }
        catch (Exception)
        {
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (SideEffect sideEffect in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (_abandoned)
                {
                    return;
                }

                switch (sideEffect.Kind)
                {
                    case SideEffectKind.Notification:
                        try
                        {
                            var args = new S7SessionStateChangedEventArgs
                            {
                                PreviousState = sideEffect.PreviousState,
                                CurrentState = sideEffect.CurrentState,
                                SessionId = sideEffect.SessionId,
                                Timestamp = _timeProvider.GetUtcNow()
                            };

                            _dispatchNotification(args);
                        }
                        catch (Exception)
                        {
                        }

                        break;

                    case SideEffectKind.FailureLog:
                        try
                        {
                            if (!_logger.IsEnabled(sideEffect.Level))
                            {
                                break;
                            }

                            S7LogEntry entry = sideEffect.Entry;
                            _logger.Log(sideEffect.Level, in entry, sideEffect.Cause);
                        }
                        catch (Exception)
                        {
                        }

                        break;

                    case SideEffectKind.RequestedFailureLog:
                        try
                        {
                            if (!_logger.IsEnabled(sideEffect.Request.Level))
                            {
                                break;
                            }

                            FailureLogRequest request = sideEffect.Request;
                            S7ValueText value = S7ValueText.None;
                            if (request.ValueTraceEnabled && request.ValueState != S7ValueState.None)
                            {
                                value = FormatValue(in request);
                            }

                            S7LogEntry entry = request.ToEntry(in value);
                            Exception? cause = request.Cause;
                            if (cause is null && request.Reason is not null)
                            {
                                cause = new ReconnectDecisionInfo(request.Reason, request.JudgmentElapsed);
                            }

                            _logger.Log(request.Level, in entry, cause);
                        }
                        catch (Exception)
                        {
                        }

                        break;
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static S7ValueText FormatValue(in FailureLogRequest request)
    {
        if (request.TypedValue is { } typed)
        {
            return S7ValueFormatter.FormatTyped(request.DataType ?? typed.DataType, in typed);
        }

        if (request.StructValue is not null)
        {
            return request.StructSnapshot is null
                ? new S7ValueText { FormatFailed = true }
                : S7ValueFormatter.FormatStruct(request.StructValue, request.StructSnapshot);
        }

        return S7ValueFormatter.FormatHex(request.ValueBytes.Span);
    }

    private enum SideEffectKind
    {
        Notification,
        FailureLog,
        RequestedFailureLog
    }

    private readonly struct SideEffect
    {
        private SideEffect(
            SideEffectKind kind,
            S7SessionState previousState,
            S7SessionState currentState,
            Guid sessionId,
            S7LogLevel level,
            S7LogEntry entry,
            Exception? cause,
            FailureLogRequest request)
        {
            Kind = kind;
            PreviousState = previousState;
            CurrentState = currentState;
            SessionId = sessionId;
            Level = level;
            Entry = entry;
            Cause = cause;
            Request = request;
        }

        internal SideEffectKind Kind { get; }

        internal S7SessionState PreviousState { get; }

        internal S7SessionState CurrentState { get; }

        internal Guid SessionId { get; }

        internal S7LogLevel Level { get; }

        internal S7LogEntry Entry { get; }

        internal Exception? Cause { get; }

        internal FailureLogRequest Request { get; }

        internal static SideEffect CreateNotification(
            S7SessionState previous,
            S7SessionState current,
            Guid sessionId)
        {
            return new SideEffect(
                SideEffectKind.Notification,
                previous,
                current,
                sessionId,
                default,
                default,
                null,
                default);
        }

        internal static SideEffect CreateFailureLog(S7LogLevel level, in S7LogEntry entry, Exception? cause)
        {
            return new SideEffect(
                SideEffectKind.FailureLog,
                default,
                default,
                Guid.Empty,
                level,
                entry,
                cause,
                default);
        }

        internal static SideEffect CreateFailureLog(in FailureLogRequest request)
        {
            return new SideEffect(
                SideEffectKind.RequestedFailureLog,
                default,
                default,
                Guid.Empty,
                request.Level,
                default,
                null,
                request);
        }
    }
}
