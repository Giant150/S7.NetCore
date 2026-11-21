namespace S7.NetCore;

/// <summary>
/// PLC 存储区域。数值与 S7 协议中的区域编码一致。
/// </summary>
public enum S7Area : byte
{
    /// <summary>过程输入区（I / PE），只读。</summary>
    Input = 0x81,

    /// <summary>过程输出区（Q / PA），可读写。</summary>
    Output = 0x82,

    /// <summary>位存储区（M），可读写。</summary>
    Memory = 0x83,

    /// <summary>数据块区（DB），可读写。</summary>
    DataBlock = 0x84
}

/// <summary>
/// 支持的 CPU 型号。数值与参考实现保持一致，便于报文对照。
/// </summary>
public enum S7CpuType
{
    /// <summary>S7-200。</summary>
    S7200 = 0,

    /// <summary>Logo 0BA8。</summary>
    Logo0BA8 = 1,

    /// <summary>S7-200 Smart。</summary>
    S7200Smart = 2,

    /// <summary>S7-300。</summary>
    S7300 = 10,

    /// <summary>S7-400。</summary>
    S7400 = 20,

    /// <summary>S7-1200。</summary>
    S71200 = 30,

    /// <summary>S7-1500。</summary>
    S71500 = 40
}

/// <summary>
/// 会话状态。
/// </summary>
public enum S7SessionState
{
    /// <summary>未连接。</summary>
    Disconnected = 0,

    /// <summary>连接中（TCP / COTP / S7 通讯设置阶段）。</summary>
    Connecting = 1,

    /// <summary>已连接，可执行读写。</summary>
    Connected = 2,

    /// <summary>会话异常，需调用方显式断开后重连。</summary>
    Faulted = 3,

    /// <summary>连接中断，正在自动重连；此状态下禁止发送读写帧。</summary>
    Reconnecting = 4
}

/// <summary>
/// 统一错误类别。
/// </summary>
public enum S7ErrorCategory
{
    /// <summary>参数或会话状态非法。</summary>
    InvalidArgument = 1,

    /// <summary>网络连接失败或连接被重置。</summary>
    Connection = 2,

    /// <summary>协议报文非法或响应不符合预期。</summary>
    Protocol = 3,

    /// <summary>PLC 返回错误码。</summary>
    PlcReturn = 4,

    /// <summary>操作超时。</summary>
    Timeout = 5,

    /// <summary>调用方取消。</summary>
    Cancelled = 6
}

/// <summary>
/// 失败发生的操作阶段。
/// </summary>
public enum S7OperationStage
{
    /// <summary>本地参数校验阶段。</summary>
    Validate = 0,

    /// <summary>TCP 连接阶段。</summary>
    Connect = 1,

    /// <summary>COTP 连接建立阶段。</summary>
    CotpConnect = 2,

    /// <summary>S7 通讯设置与 PDU 协商阶段。</summary>
    S7Setup = 3,

    /// <summary>读取阶段。</summary>
    Read = 4,

    /// <summary>写入阶段。</summary>
    Write = 5,

    /// <summary>断开阶段。</summary>
    Disconnect = 6,

    /// <summary>自动重连阶段。</summary>
    Reconnect = 7
}

/// <summary>
/// 稳定的错误码。数值一经发布不得修改或复用，新增只追加。
/// </summary>
public enum S7ErrorCode
{
    /// <summary>IP 地址非法。</summary>
    InvalidAddress = 100,

    /// <summary>端口超出 1 至 65535。</summary>
    InvalidPort = 101,

    /// <summary>CPU 型号未定义。</summary>
    InvalidCpuType = 102,

    /// <summary>机架号或槽位号超出 0 至 15。</summary>
    InvalidRackSlot = 103,

    /// <summary>超时时长超出允许范围。</summary>
    InvalidTimeout = 104,

    /// <summary>PDU 申请值超出 240 至 960。</summary>
    InvalidPduSize = 105,

    /// <summary>重连策略项非法（尝试次数、退避倍数、抖动比例或上下限关系不满足）。</summary>
    InvalidReconnectPolicy = 106,

    /// <summary>DB 号非法。</summary>
    InvalidDbNumber = 110,

    /// <summary>起始字节地址非法。</summary>
    InvalidStartByte = 111,

    /// <summary>读写长度非法。</summary>
    InvalidLength = 112,

    /// <summary>写入数据为空或超长。</summary>
    InvalidData = 113,

    /// <summary>区域不受本期支持（含 Timer / Counter 与写入 Input）。</summary>
    AreaNotSupported = 120,

    /// <summary>会话未建立或已处于异常状态。</summary>
    SessionNotEstablished = 200,

    /// <summary>会话已连接时重复连接。</summary>
    SessionAlreadyConnected = 201,

    /// <summary>会话处于异常状态。</summary>
    SessionFaulted = 202,

    /// <summary>请求队列超出上限。</summary>
    QueueOverflow = 203,

    /// <summary>连接中断，正在自动重连且等待窗口已到期或为零。</summary>
    SessionReconnecting = 204,

    /// <summary>TCP 连接失败。</summary>
    TcpConnectFailed = 300,

    /// <summary>连接被重置或已断开。</summary>
    ConnectionReset = 301,

    /// <summary>COTP 连接被拒绝。</summary>
    CotpConnectionRejected = 302,

    /// <summary>S7 通讯设置响应非法。</summary>
    InvalidSetupResponse = 400,

    /// <summary>TPKT 帧非法。</summary>
    InvalidFrame = 401,

    /// <summary>COTP 帧非法。</summary>
    InvalidCotpFrame = 402,

    /// <summary>S7 报文头非法。</summary>
    InvalidS7Header = 403,

    /// <summary>响应数据长度不足。</summary>
    ResponseTooShort = 404,

    /// <summary>PLC 返回码不符合预期语义。</summary>
    UnexpectedReturnCode = 405,

    /// <summary>PLC 硬件故障（0x01）。</summary>
    PlcHardwareFault = 500,

    /// <summary>PLC 不允许访问（0x03）。</summary>
    PlcAccessDenied = 501,

    /// <summary>PLC 地址越界（0x05）。</summary>
    PlcAddressOutOfRange = 502,

    /// <summary>PLC 数据类型不受支持（0x06）。</summary>
    PlcDataTypeNotSupported = 503,

    /// <summary>PLC 数据不一致（0x07）。</summary>
    PlcDataInconsistent = 504,

    /// <summary>PLC 对象不存在（0x0A）。</summary>
    PlcObjectNotExist = 505,

    /// <summary>PLC 返回未知返回码。</summary>
    PlcUnknownReturnCode = 599,

    /// <summary>连接超时。</summary>
    ConnectTimeout = 600,

    /// <summary>读取超时。</summary>
    ReadTimeout = 601,

    /// <summary>写入超时。</summary>
    WriteTimeout = 602,

    /// <summary>调用方取消。</summary>
    OperationCancelled = 603,

    /// <summary>重连尝试次数耗尽，会话回到未连接。</summary>
    ReconnectExhausted = 604,

    /// <summary>重连被显式断开或客户端释放中止。</summary>
    ReconnectAborted = 605,

    /// <summary>地址表达式语法非法（空、内部空白、长度超过 64、非 ASCII、负号、非法分隔符或后缀组合）。</summary>
    AddressSyntaxInvalid = 130,

    /// <summary>位号越界或位号使用位置非法（仅 X/位形式可携带 0～7 位号）。</summary>
    BitNumberOutOfRange = 131,

    /// <summary>数据类型不受支持（十一种封闭集合之外：DateTime、Struct、UDT、数组等）。</summary>
    DataTypeNotSupported = 132,

    /// <summary>数据类型与地址访问宽度不兼容。</summary>
    DataTypeAddressMismatch = 133,

    /// <summary>写入值超出目标类型范围。</summary>
    ValueOutOfRange = 134,

    /// <summary>写入字符串包含非 ASCII 字符。</summary>
    StringEncodingInvalid = 135,

    /// <summary>字符串容量非法或内容长度超过容量。</summary>
    StringCapacityInvalid = 136,

    /// <summary>DB 号、偏移或类型占用范围超出区域容量。</summary>
    AddressOutOfRange = 137,

    /// <summary>写入值的种类与目标数据类型不匹配。</summary>
    ValueTypeMismatch = 138,

    /// <summary>读回的 S7 String 头部非法（长度超过容量等）。</summary>
    InvalidS7StringHeader = 406,

    /// <summary>读回的字符串内容包含非 ASCII 字节。</summary>
    NonAsciiStringData = 407,

    /// <summary>
    /// S7WString 头部非法（头部容量不在 1～16383、头部容量大于请求容量、声明长度大于头部容量等）。
    /// 类别 Protocol，阶段 Read；不复用 406/407。
    /// </summary>
    InvalidS7WStringHeader = 408,

    /// <summary>
    /// S7WString 字符数据非法（孤立代理项、末尾不完整代理、字符区不足等）。
    /// 类别 Protocol，阶段 Read；不复用 406/407。
    /// </summary>
    InvalidS7WStringData = 409,

    /// <summary>
    /// 结构体布局非法（空布局、字段标注位置或数量非法、容量越界、总长越界、显式偏移等）。
    /// 类别 InvalidArgument，阶段 Validate。
    /// </summary>
    StructLayoutInvalid = 139,

    /// <summary>
    /// 结构体地址不受支持（非 DB 区、非 DBB 宽度、带位号或 DB 号小于 1）。
    /// 类别 InvalidArgument，阶段 Validate。
    /// </summary>
    StructAddressNotSupported = 140
}