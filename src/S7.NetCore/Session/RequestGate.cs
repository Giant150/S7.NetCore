namespace S7.NetCore.Internal;

/// <summary>
/// 请求闸门：单许可信号量实现串行化，带排队上限与可取消等待。
/// </summary>
internal sealed class RequestGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _maxQueuedRequests;
    private int _pending;
    private int _disposed;

    /// <summary>
    /// 初始化请求闸门。
    /// </summary>
    /// <param name="maxQueuedRequests">最大排队请求数。</param>
    internal RequestGate(int maxQueuedRequests)
    {
        _maxQueuedRequests = maxQueuedRequests;
    }

    /// <summary>
    /// 获取请求许可。
    /// </summary>
    /// <param name="stage">操作阶段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>许可租约，释放时归还信号量。</returns>
    /// <exception cref="S7ClientException">队列溢出时抛出。</exception>
    internal async ValueTask<Lease> AcquireAsync(S7OperationStage stage, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        int pending = Interlocked.Increment(ref _pending);
        if (pending > _maxQueuedRequests + 1)
        {
            Interlocked.Decrement(ref _pending);
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.QueueOverflow,
                stage,
                $"请求队列已满（上限 {_maxQueuedRequests} 个等待请求），请稍后重试。");
        }

        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }

        return new Lease(this);
    }

    /// <summary>
    /// 尝试非阻塞获取请求许可。
    /// </summary>
    /// <param name="stage">操作阶段。</param>
    /// <param name="lease">取得许可时返回租约，否则为 <see langword="null"/>。</param>
    /// <returns>是否立即取得许可。</returns>
    /// <exception cref="S7ClientException">队列溢出时抛出。</exception>
    internal bool TryAcquire(S7OperationStage stage, out Lease? lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lease = null;
        if (!_semaphore.Wait(0))
        {
            return false;
        }

        int pending = Interlocked.Increment(ref _pending);
        if (pending > _maxQueuedRequests + 1)
        {
            Interlocked.Decrement(ref _pending);
            _semaphore.Release();
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.QueueOverflow,
                stage,
                $"请求队列已满（上限 {_maxQueuedRequests} 个等待请求），请稍后重试。");
        }

        lease = new Lease(this);
        return true;
    }

    /// <summary>
    /// 获取请求许可，同时响应调用方取消与内部中断信号。
    /// </summary>
    /// <param name="stage">操作阶段。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <param name="interruptToken">内部中断令牌（连接丢失或会话释放）。</param>
    /// <returns>许可租约，释放时归还信号量。</returns>
    /// <exception cref="S7ClientException">队列溢出时抛出。</exception>
    internal async ValueTask<Lease> AcquireAsync(
        S7OperationStage stage,
        CancellationToken cancellationToken,
        CancellationToken interruptToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        int pending = Interlocked.Increment(ref _pending);
        if (pending > _maxQueuedRequests + 1)
        {
            Interlocked.Decrement(ref _pending);
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.QueueOverflow,
                stage,
                $"请求队列已满（上限 {_maxQueuedRequests} 个等待请求），请稍后重试。");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, interruptToken);
        try
        {
            await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }

        return new Lease(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _semaphore.Dispose();
    }

    /// <summary>
    /// 请求许可租约。
    /// </summary>
    internal sealed class Lease : IDisposable
    {
        private readonly RequestGate _gate;
        private int _disposed;

        internal Lease(RequestGate gate)
        {
            _gate = gate;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Decrement(ref _gate._pending);
            _gate._semaphore.Release();
        }
    }
}