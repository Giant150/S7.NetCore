namespace S7.NetCore.Internal;

/// <summary>
/// 操作超时编排：链接调用方令牌与超时令牌，并区分触发源。
/// </summary>
internal sealed class OperationTimeout : IDisposable
{
    private readonly CancellationToken _callerToken;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenSource _linkedSource;
    private int _disposed;

    /// <summary>
    /// 初始化超时编排。
    /// </summary>
    /// <param name="timeout">超时时长。</param>
    /// <param name="callerToken">调用方取消令牌。</param>
    /// <param name="timeProvider">时间提供者。</param>
    internal OperationTimeout(TimeSpan timeout, CancellationToken callerToken, TimeProvider timeProvider)
    {
        _callerToken = callerToken;
        _timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        _linkedSource = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _timeoutSource.Token);
    }

    /// <summary>合成取消令牌。</summary>
    internal CancellationToken Token => _linkedSource.Token;

    /// <summary>调用方是否已触发取消。</summary>
    internal bool CallerTriggered => _callerToken.IsCancellationRequested;

    /// <summary>是否为超时触发（而非调用方取消）。</summary>
    internal bool TimedOut => !CallerTriggered && _timeoutSource.IsCancellationRequested;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _linkedSource.Dispose();
        _timeoutSource.Dispose();
    }
}