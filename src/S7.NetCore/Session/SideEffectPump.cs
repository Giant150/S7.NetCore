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
    private readonly Task _worker;
    private volatile bool _abandoned;

    internal SideEffectPump(IS7Logger logger, Action<S7SessionStateChangedEventArgs> dispatchNotification)
    {
        _logger = logger;
        _dispatchNotification = dispatchNotification;
        _queue = Channel.CreateUnbounded<SideEffect>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(RunAsync);
    }

    internal void PostNotification(S7SessionStateChangedEventArgs args)
    {
        _queue.Writer.TryWrite(SideEffect.CreateNotification(args));
    }

    internal void PostFailureLog(S7LogLevel level, in S7LogEntry entry, Exception? cause)
    {
        _queue.Writer.TryWrite(SideEffect.CreateFailureLog(level, entry, cause));
    }

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
                            _dispatchNotification(sideEffect.Notification!);
                        }
                        catch (Exception)
                        {
                        }

                        break;

                    case SideEffectKind.FailureLog:
                        try
                        {
                            S7LogEntry entry = sideEffect.Entry;
                            _logger.Log(sideEffect.Level, in entry, sideEffect.Cause);
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

    private enum SideEffectKind
    {
        Notification,
        FailureLog
    }

    private readonly struct SideEffect
    {
        private SideEffect(
            SideEffectKind kind,
            S7SessionStateChangedEventArgs? notification,
            S7LogLevel level,
            S7LogEntry entry,
            Exception? cause)
        {
            Kind = kind;
            Notification = notification;
            Level = level;
            Entry = entry;
            Cause = cause;
        }

        internal SideEffectKind Kind { get; }

        internal S7SessionStateChangedEventArgs? Notification { get; }

        internal S7LogLevel Level { get; }

        internal S7LogEntry Entry { get; }

        internal Exception? Cause { get; }

        internal static SideEffect CreateNotification(S7SessionStateChangedEventArgs args)
        {
            return new SideEffect(SideEffectKind.Notification, args, default, default, null);
        }

        internal static SideEffect CreateFailureLog(S7LogLevel level, in S7LogEntry entry, Exception? cause)
        {
            return new SideEffect(SideEffectKind.FailureLog, null, level, entry, cause);
        }
    }
}