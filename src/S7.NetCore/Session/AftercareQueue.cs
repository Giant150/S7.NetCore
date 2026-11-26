using System.Collections.Concurrent;

namespace S7.NetCore.Internal;

/// <summary>
/// 迟到响应善后队列：失败返回路径只登记，排空由下一次持许可调用执行。
/// </summary>
internal sealed class AftercareQueue
{
    private readonly ConcurrentQueue<PendingLateResponse> _items = new();

    /// <summary>队列项数（仅测试可见）。</summary>
    internal int Count => _items.Count;

    /// <summary>
    /// 登记一项迟到响应善后需求。非阻塞、无 I/O。
    /// </summary>
    /// <param name="item">善后项。</param>
    internal void Post(in PendingLateResponse item)
    {
        _items.Enqueue(item);
    }

    /// <summary>
    /// 尝试取出一项善后需求。
    /// </summary>
    /// <param name="item">取出的善后项。</param>
    /// <returns>队列非空返回 <see langword="true"/>。</returns>
    internal bool TryTake(out PendingLateResponse item)
    {
        return _items.TryDequeue(out item);
    }

    /// <summary>清空全部善后项（断开、释放或重连接管旧连接时调用）。</summary>
    internal void Clear()
    {
        while (_items.TryDequeue(out _))
        {
        }
    }
}

/// <summary>
/// 迟到响应善后项：纯数据载体，不含任何 I/O 能力。
/// </summary>
internal readonly struct PendingLateResponse
{
    /// <summary>
    /// 初始化善后项。
    /// </summary>
    /// <param name="transport">已发帧的传输实例。</param>
    /// <param name="pduReference">期望消费的 PDU 引用。</param>
    /// <param name="stage">失败阶段。</param>
    /// <param name="drainTimeout">排空超时。</param>
    /// <param name="connectionEpoch">连接代标识。</param>
    internal PendingLateResponse(
        ITcpTransport transport,
        ushort pduReference,
        S7OperationStage stage,
        TimeSpan drainTimeout,
        long connectionEpoch)
    {
        Transport = transport;
        PduReference = pduReference;
        Stage = stage;
        DrainTimeout = drainTimeout;
        ConnectionEpoch = connectionEpoch;
    }

    /// <summary>已发帧的传输实例。</summary>
    internal ITcpTransport Transport { get; }

    /// <summary>期望消费的 PDU 引用。</summary>
    internal ushort PduReference { get; }

    /// <summary>失败阶段。</summary>
    internal S7OperationStage Stage { get; }

    /// <summary>排空超时。</summary>
    internal TimeSpan DrainTimeout { get; }

    /// <summary>连接代标识，换代即丢弃。</summary>
    internal long ConnectionEpoch { get; }
}
