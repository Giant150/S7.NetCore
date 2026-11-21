namespace S7.NetCore;

/// <summary>
/// 传输层服务访问点（TSAP），由两个字节组成。
/// </summary>
/// <param name="FirstByte">第一个字节。</param>
/// <param name="SecondByte">第二个字节。</param>
public readonly record struct Tsap(byte FirstByte, byte SecondByte);

/// <summary>
/// S7 连接参数。构造后不可变，由 <see cref="S7Client"/> 在创建时完成校验。
/// </summary>
public sealed record S7ConnectionOptions
{
    /// <summary>PLC 的 IPv4 或 IPv6 地址。必填，不接受主机名。</summary>
    public required string IpAddress { get; init; }

    /// <summary>PLC 的 CPU 型号。必填。</summary>
    public required S7CpuType CpuType { get; init; }

    /// <summary>服务端口，取值范围 1 至 65535，默认 102。</summary>
    public int Port { get; init; } = 102;

    /// <summary>机架号，取值范围 0 至 15，默认 0。</summary>
    public int Rack { get; init; }

    /// <summary>槽位号，取值范围 0 至 15，默认 0。</summary>
    public int Slot { get; init; }

    /// <summary>本地 TSAP；为 <see langword="null"/> 时按 CPU 型号推导。</summary>
    public Tsap? LocalTsap { get; init; }

    /// <summary>远端 TSAP；为 <see langword="null"/> 时按 CPU 型号与机架/槽位推导。</summary>
    public Tsap? RemoteTsap { get; init; }

    /// <summary>连接超时，取值范围 1 至 120 秒，默认 5 秒。</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>单次读取超时，取值范围 1 至 300 秒，默认 10 秒。</summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>单次写入超时，取值范围 1 至 300 秒，默认 10 秒。</summary>
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>通讯设置阶段申请的 PDU 大小，取值范围 240 至 960，默认 480。</summary>
    public int RequestedPduSize { get; init; } = 480;

    /// <summary>同会话请求队列上限，取值范围 1 至 1024，默认 32。</summary>
    public int MaxQueuedRequests { get; init; } = 32;

    /// <summary>是否输出帧级十六进制协议跟踪。默认关闭。</summary>
    public bool EnableProtocolTrace { get; init; }
}