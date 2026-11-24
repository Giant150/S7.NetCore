namespace S7.NetCore.Internal;

/// <summary>
/// 协议层常量。读写净荷阈值的唯一来源。
/// </summary>
internal static class ProtocolConstants
{
    /// <summary>TPKT 帧头长度。</summary>
    internal const int TpktHeaderSize = 4;

    /// <summary>COTP DT 头长度（含长度字节）。</summary>
    internal const int CotpDataHeaderSize = 3;

    /// <summary>S7 请求头长度。</summary>
    internal const int S7JobHeaderSize = 10;

    /// <summary>S7 Ack_Data 响应头长度。</summary>
    internal const int S7AckHeaderSize = 12;

    /// <summary>S7 Job 净荷头长度（S7 头 10 + 功能码 1 + 条目计数 1）。</summary>
    internal const int S7JobPayloadHeaderSize = S7JobHeaderSize + 2;

    /// <summary>单条读/写参数条目头长度。</summary>
    internal const int ReadItemHeaderSize = 12;

    /// <summary>写数据条目头长度。</summary>
    internal const int WriteItemDataHeaderSize = 4;

    /// <summary>S7 Ack 净荷头长度（Ack 头 12 + 功能码 1 + 条目计数 1）。</summary>
    internal const int S7AckPayloadHeaderSize = S7AckHeaderSize + 2;

    /// <summary>读响应每项结果头长度（返回码 1 + 传输尺寸 1 + 长度 2）。</summary>
    internal const int ReadItemResultHeaderSize = 4;

    /// <summary>写响应每项结果码长度。</summary>
    internal const int WriteItemResultCodeSize = 1;

    /// <summary>单条读请求净荷固定开销（Job 净荷头 + 条目头）。</summary>
    internal const int ReadRequestPayloadOverhead = S7JobPayloadHeaderSize + ReadItemHeaderSize;

    /// <summary>单条读响应净荷固定开销（Ack 净荷头 + 单项结果头）。</summary>
    internal const int ReadResponseOverhead = S7AckPayloadHeaderSize + ReadItemResultHeaderSize;

    /// <summary>单条写请求净荷固定开销（Job 净荷头 + 条目头 + 数据头）。</summary>
    internal const int WriteRequestPayloadOverhead = S7JobPayloadHeaderSize + ReadItemHeaderSize + WriteItemDataHeaderSize;

    /// <summary>单条写响应净荷固定开销（Ack 净荷头，每项另计 1 字节返回码）。</summary>
    internal const int WriteResponsePayloadOverhead = S7AckPayloadHeaderSize;

    /// <summary>单条读请求完整帧长度。</summary>
    internal const int ReadRequestFrameSize = TpktHeaderSize + CotpDataHeaderSize + ReadRequestPayloadOverhead;

    /// <summary>写请求完整帧固定开销（TPKT + COTP + 写请求净荷开销）。</summary>
    internal const int WriteRequestOverhead = TpktHeaderSize + CotpDataHeaderSize + WriteRequestPayloadOverhead;

    /// <summary>协议条目计数字段可表达的最大条目数。</summary>
    internal const int MaxBatchItemCount = 255;

    /// <summary>最小 PDU 大小。</summary>
    internal const int MinPduSize = 240;

    /// <summary>PDU 协商缺失时的回退值。</summary>
    internal const int DefaultPduSize = 240;

    /// <summary>默认 PDU 申请值。</summary>
    internal const int DefaultRequestedPduSize = 480;

    /// <summary>PDU 申请值上限。</summary>
    internal const int MaxRequestedPduSize = 960;

    /// <summary>单帧接收缓冲上限，防止对端伪造超长帧。</summary>
    internal const int MaxFrameSize = 2048;

    /// <summary>单次读写最大字节数。</summary>
    internal const int MaxTransferLength = 65535;

    /// <summary>最大起始字节地址。</summary>
    internal const int MaxStartByte = 65535;

    /// <summary>最大机架号与槽位号。</summary>
    internal const int MaxRackSlot = 15;

    /// <summary>S7 协议标识。</summary>
    internal const byte S7ProtocolId = 0x32;

    /// <summary>S7 请求报文类型（Job）。</summary>
    internal const byte S7RosctrJob = 0x01;

    /// <summary>S7 响应报文类型（Ack_Data）。</summary>
    internal const byte S7RosctrAckData = 0x03;

    /// <summary>读功能码。</summary>
    internal const byte S7FunctionRead = 0x04;

    /// <summary>写功能码。</summary>
    internal const byte S7FunctionWrite = 0x05;

    /// <summary>通讯设置功能码。</summary>
    internal const byte S7FunctionSetup = 0xF0;

    /// <summary>传输尺寸：字节。</summary>
    internal const byte TransportSizeByte = 0x02;

    /// <summary>传输尺寸：八位组（响应数据）。</summary>
    internal const byte TransportSizeOctet = 0x04;

    /// <summary>PLC 成功返回码。</summary>
    internal const byte SuccessReturnCode = 0xFF;

    /// <summary>COTP 连接确认报文类型。</summary>
    internal const byte CotpPduTypeConnectionConfirm = 0xD0;

    /// <summary>COTP 数据传输报文类型。</summary>
    internal const byte CotpPduTypeData = 0xF0;

    /// <summary>COTP 最后分段标志。</summary>
    internal const byte CotpLastDataUnit = 0x80;

    /// <summary>TPKT 版本号。</summary>
    internal const byte TpktVersion = 0x03;

    /// <summary>COTP CR 完整帧长度。</summary>
    internal const int CotpConnectionRequestSize = 22;

    /// <summary>S7 通讯设置请求完整帧长度。</summary>
    internal const int SetupCommunicationFrameSize = 25;

    /// <summary>写参数区传输尺寸：位。</summary>
    internal const byte BitTransportSizeParameter = 0x01;

    /// <summary>写数据区传输尺寸：位。</summary>
    internal const byte BitTransportSizeData = 0x03;

    /// <summary>单请求位写完整帧长度（写请求固定开销 + 1 字节位值）。</summary>
    internal const int WriteBitRequestFrameSize = WriteRequestOverhead + 1;
}