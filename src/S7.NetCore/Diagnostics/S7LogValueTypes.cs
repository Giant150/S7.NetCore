namespace S7.NetCore;

/// <summary>日志值文本的格式。</summary>
public enum S7ValueFormat : byte
{
    /// <summary>不适用或未提供值。</summary>
    None = 0,

    /// <summary>原始字节，大写无分隔十六进制。</summary>
    Hex = 1,

    /// <summary>类型化单值，形如 <c>类型=值</c>。</summary>
    Typed = 2,

    /// <summary>结构体单行 JSON。</summary>
    Json = 3,

    /// <summary>批量逐条值列表。</summary>
    Mixed = 4
}

/// <summary>日志值状态。</summary>
public enum S7ValueState : byte
{
    /// <summary>不适用（例如读取失败、会话类操作或值展示关闭）。</summary>
    None = 0,

    /// <summary>值已确认（读取成功或写入成功）。</summary>
    Confirmed = 1,

    /// <summary>请求值未发送（本地校验或编码失败）。</summary>
    NotSent = 2,

    /// <summary>请求值可能已发送，写入结果不确定。</summary>
    Uncertain = 3
}

/// <summary>协议帧跟踪方向。</summary>
public enum S7ProtocolTraceDirection : byte
{
    /// <summary>非协议帧条目。</summary>
    None = 0,

    /// <summary>发送帧。</summary>
    Tx = 1,

    /// <summary>接收帧。</summary>
    Rx = 2
}