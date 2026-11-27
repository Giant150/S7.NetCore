using S7.NetCore.Internal;

namespace S7.NetCore;

/// <summary>
/// 西门子 S7 PLC 字节通讯客户端。提供会话建立/断开、I/Q/M/DB 原始字节读写与可选的断线自动重连。
/// </summary>
public sealed partial class S7Client : IAsyncDisposable
{
    private readonly S7Session _session;
    private readonly IS7Logger _logger;
    private readonly S7ConnectionOptions _options;
    private EventHandler<S7SessionStateChangedEventArgs>? _sessionStateChanged;

    /// <summary>
    /// 创建客户端并校验连接参数。
    /// </summary>
    /// <param name="options">连接参数。</param>
    /// <param name="logger">可选诊断日志设施；不注入时使用空实现。</param>
    /// <param name="transport">可选传输实现；不注入时使用托管 TCP 实现（主要供测试替换）。</param>
    /// <param name="transportFactory">可选传输工厂；启用自动重连时建议注入，每次重连将获取新实例。</param>
    /// <param name="timeProvider">可选时间源；不注入时使用系统时间。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="S7ClientException">连接参数非法时抛出，类别为 <see cref="S7ErrorCategory.InvalidArgument"/>。</exception>
    public S7Client(
        S7ConnectionOptions options,
        IS7Logger? logger = null,
        ITcpTransport? transport = null,
        Func<ITcpTransport>? transportFactory = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        OptionsValidator.Validate(options);
        _options = options;
        _logger = logger is null or NullS7Logger ? NullS7Logger.Instance : new SafeS7Logger(logger);
        _session = new S7Session(options, logger, transport, timeProvider, transportFactory);
        _session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>当前会话状态。</summary>
    public S7SessionState State => _session.State;

    /// <summary>会话是否已连接。</summary>
    public bool IsConnected => State == S7SessionState.Connected;

    /// <summary>协商得到的 PDU 大小；未连接时为 0。</summary>
    public int NegotiatedPduSize => _session.NegotiatedPduSize;

    /// <summary>会话状态的一致快照。</summary>
    public SessionSnapshot Snapshot => _session.Snapshot;

    /// <summary>会话状态迁移通知；订阅方异常不会影响主流程。</summary>
    public event EventHandler<S7SessionStateChangedEventArgs>? SessionStateChanged
    {
        add => _sessionStateChanged += value;
        remove => _sessionStateChanged -= value;
    }

    /// <summary>
    /// 建立会话（TCP → COTP → S7 通讯设置与 PDU 协商）。显式连接失败不触发自动重连。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接完成的任务。</returns>
    /// <exception cref="S7ClientException">
    /// 参数或状态非法（<see cref="S7ErrorCategory.InvalidArgument"/>）、
    /// 网络失败（<see cref="S7ErrorCategory.Connection"/>）、协议异常（<see cref="S7ErrorCategory.Protocol"/>）、
    /// 超时（<see cref="S7ErrorCategory.Timeout"/>）或取消（<see cref="S7ErrorCategory.Cancelled"/>）。
    /// </exception>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _session.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// 断开会话。重连中调用会中止重连；未连接或已异常时调用同样成功（幂等）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>断开完成的任务。</returns>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _session.DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// 读取原始字节。超过协商 PDU 时自动分包，返回长度恒等于请求长度；任一子请求失败则整体失败。
    /// </summary>
    /// <param name="request">读取请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与请求长度一致的原始字节序列。</returns>
    /// <exception cref="S7ClientException">
    /// 请求非法（<see cref="S7ErrorCategory.InvalidArgument"/>）、会话未建立、
    /// PLC 返回错误（<see cref="S7ErrorCategory.PlcReturn"/>）、协议异常或超时/取消。
    /// </exception>
    public Task<ReadOnlyMemory<byte>> ReadBytesAsync(
        S7ReadRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.ValidateRead(in request);
        return ReadBytesCoreAsync(request, cancellationToken);
    }

    private Task<ReadOnlyMemory<byte>> ReadBytesCoreAsync(
        S7ReadRequest request,
        CancellationToken cancellationToken)
    {
        return _session.ReadAsync(request, cancellationToken);
    }

    /// <summary>
    /// 写入原始字节。超过协商 PDU 时自动分包，成功返回实际写入字节数（等于数据长度）。
    /// </summary>
    /// <param name="request">写入请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际写入的字节数。</returns>
    /// <exception cref="S7ClientException">
    /// 请求非法（<see cref="S7ErrorCategory.InvalidArgument"/>）、会话未建立、
    /// PLC 返回错误（<see cref="S7ErrorCategory.PlcReturn"/>）、协议异常或超时/取消。
    /// 失败时异常属性 <see cref="S7ClientException.BytesWritten"/> 与
    /// <see cref="S7ClientException.WrittenRange"/> 携带已确认写入进度。
    /// </exception>
    public Task<int> WriteBytesAsync(
        S7WriteRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestValidator.ValidateWrite(in request);
        return WriteBytesCoreAsync(request, cancellationToken);
    }

    private Task<int> WriteBytesCoreAsync(
        S7WriteRequest request,
        CancellationToken cancellationToken)
    {
        return _session.WriteAsync(request, cancellationToken);
    }

    /// <summary>
    /// 幂等释放会话与传输资源。
    /// </summary>
    /// <returns>释放完成的任务。</returns>
    public ValueTask DisposeAsync()
    {
        return _session.DisposeAsync();
    }

    private void OnSessionStateChanged(object? sender, S7SessionStateChangedEventArgs e)
    {
        EventHandler<S7SessionStateChangedEventArgs>? handlers = _sessionStateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<S7SessionStateChangedEventArgs>)handler)(this, e);
            }
            catch (Exception exception)
            {
                LogNotificationFailure(exception);
            }
        }
    }

    private void LogNotificationFailure(Exception exception)
    {
        if (!_logger.IsEnabled(S7LogLevel.Warning))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = "Session",
            Stage = S7OperationStage.Reconnect,
            Succeeded = false,
            ErrorCode = S7ErrorCode.SessionReconnecting
        };

        _logger.Log(S7LogLevel.Warning, in entry, exception);
    }
}