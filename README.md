# S7.NetCore

基于 .NET 8 / 9 / 10 / 11 的西门子 S7 PLC 通讯组件，全托管实现。提供会话管理、原始字节读写、十一种数据类型的类型化读写，以及扁平 C# 结构体读写。

## 目标框架支持矩阵

| 目标框架 | 可用状态 | 验证方式 | 依赖主版本 | 最低使用方框架 | 说明 |
|----------|---------|---------|-----------|---------------|------|
| net8.0 | 支持 | 仅构建验证 | 8 | net8.0 | 最低支持框架；net7.0 及以下不受支持 |
| net9.0 | 支持 | 仅构建验证 | 9 | net9.0 | 与其它框架共享同一份源码 |
| net10.0 | 支持 | 完整测试验证 | 10 | net10.0 | 测试基准框架；全量用例在此执行 |
| net11.0 | 支持 | 仅构建验证 | 11（共享框架内置） | net11.0 | 基于 GA 版本，不承诺预览版 |

四个框架在单个 NuGet 包内均包含完整资产，由使用方按目标框架自动选择；仅构建验证不代表能力差异，仅表示测试执行范围。

## 本期范围

**已实现**

- 会话建立与关闭：TCP → COTP（CR/CC）→ S7 通讯设置与 PDU 协商
- 存储区域原始字节读写：过程输入区（I）、过程输出区（Q）、位存储区（M）、数据块区（DB）
- 超 PDU 自动分包：读取按 `PDU − 18`、写入按 `PDU − 28` 拆分（S7 净荷口径，不含 TPKT 4 + COTP 3 头；实际帧长再加 7 字节）
- 读取全有或全无；写入失败/取消可精确上报已写字节数与区间
- 取消不破坏会话（迟到响应清理）；连接/读/写超时独立可配
- 地址表达式解析：`DB1.DBW0`、`DB1.DBB0`、`DB1.DBD0`、`DB1.DBX0.3`、`MW10`、`Q0.3` 等
- 十一种类型转换：Bool、Byte、Word、Int、DWord、DInt、Real、LReal、String、S7String、S7WString
- 类型化读写与单请求位写（只改变目标位，1 次往返）
- 单请求多条目批量读写：字节视图 `ReadBatchAsync`/`WriteBatchAsync` 与类型化视图 `ReadTypedBatchAsync`/`WriteTypedBatchAsync`，一次调用只产生 1 个请求帧与 1 个响应帧；条目数由当前协商 PDU 容量与协议 1 字节计数字段（255）共同决定，超出整批拒绝并由调用方按容量分组；部分失败默认不抛异常
- 扁平结构体读写：公共实例字段按声明顺序连续映射，支持数值与带容量标注的字符串字段
- 统一错误模型（六类错误 + 稳定错误码 + PLC 原始返回码）
- 结构化诊断日志、可选帧级协议跟踪、日志失败隔离
- 断线自动重连（默认开启，可用 `AutoReconnectEnabled` 关闭；连接类故障触发，首次立即重连、失败后固定退避，重连期间读写立即返回 204）
- 纯托管实现，无 `DllImport`、无原生 Snap7 库、无第三方 S7 通讯库

**明确不做**

- DateTime、嵌套结构体/UDT、数组与嵌套类型
- 符号寻址与变量表导入
- Timer / Counter 区域
- PLC 时钟、SZL、块列表/上传/下载、CPU 启停
- PLC 服务端模拟、连接池

## 快速开始

```csharp
using S7.NetCore;

var options = new S7ConnectionOptions
{
    IpAddress = "192.168.0.10",
    CpuType = S7CpuType.S71200,
    Rack = 0,
    Slot = 1
};

await using var client = new S7Client(options);
await client.ConnectAsync();

// 读取 DB1 起始 0 的 1024 字节（自动分包）
ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
    new S7ReadRequest(S7Area.DataBlock, dbNumber: 1, startByte: 0, length: 1024));

// 写入 DB1 起始 0
byte[] payload = [0x01, 0x02, 0x03, 0x04];
int written = await client.WriteBytesAsync(
    new S7WriteRequest(S7Area.DataBlock, dbNumber: 1, startByte: 0, payload));

Console.WriteLine($"PDU={client.NegotiatedPduSize}, written={written}");
await client.DisconnectAsync();
```

非数据块区域必须将 `DbNumber` 设为 0：

```csharp
ReadOnlyMemory<byte> inputs = await client.ReadBytesAsync(
    new S7ReadRequest(S7Area.Input, dbNumber: 0, startByte: 0, length: 16));

await client.WriteBytesAsync(
    new S7WriteRequest(S7Area.Output, dbNumber: 0, startByte: 0, payload));
```

类型化读写：

```csharp
// 读取 DB1.DBW0 的 Int 值
S7TypedReadResult intValue = await client.ReadValueAsync(
    new S7TypedReadRequest("DB1.DBW0", S7DataType.Int));

// 写入 DB2.DBW0
await client.WriteValueAsync(
    new S7TypedWriteRequest("DB2.DBW0", S7DataType.Int, S7Value.FromInt16(-12345)));

// S7String（容量 16）
await client.WriteValueAsync(
    new S7TypedWriteRequest(
        "DB2.DBB16",
        S7DataType.S7String,
        S7Value.FromString("S7-PLC"),
        Capacity: 16));

// 位读取与位写
bool bit = (await client.ReadValueAsync(
    new S7TypedReadRequest("DB1.DBX0.3", S7DataType.Bool))).Value.AsBoolean();

await client.WriteValueAsync(
    new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)));
```

## 类型化读写

### 支持的类型

S7WString 读写示例（容量按 UTF-16 码元计）：

```csharp
// 容量 16，占用 4 + 2 × 16 = 36 字节
await client.WriteValueAsync(
    new S7TypedWriteRequest(
        "DB2.DBB64",
        S7DataType.S7WString,
        S7Value.FromString("温度A1"),
        Capacity: 16));

S7TypedReadResult text = await client.ReadValueAsync(
    new S7TypedReadRequest("DB2.DBB64", S7DataType.S7WString, Capacity: 16));

// 新后缀：容量随地址声明，Capacity 省略（或与地址容量一致）
await client.WriteValueAsync(
    new S7TypedWriteRequest(
        "DB2.DBR64.16",
        S7DataType.String,
        S7Value.FromString("HELLO")));

S7TypedReadResult s7Text = await client.ReadValueAsync(
    new S7TypedReadRequest("DB2.DBS64.16", S7DataType.S7String));
```

| 类型 | C# 类型 | 占用字节 | 允许后缀 | 范围 / 编码 |
|------|--------|---------|---------|------------|
| `Bool` | `bool` | 1（位语义，字节载体） | `X` 或位形式 | 0/1；读取 `(byte >> bit) & 1` |
| `Byte` | `byte` | 1 | `B` | 0～255 |
| `Word` | `ushort` | 2 | `W` | 0～65535；大端 |
| `Int` | `short` | 2 | `W` | -32768～32767；补码；大端 |
| `DWord` | `uint` | 4 | `D` | 0～4294967295；大端 |
| `DInt` | `int` | 4 | `D` | -2147483648～2147483647；补码；大端 |
| `Real` | `float` | 4 | `D` | IEEE754；大端；NaN/±Inf 位模式保持 |
| `LReal` | `double` | 8 | `D` | IEEE754；大端 |
| `String` | `string` | `Capacity` | `B`；`DBR` + `.容量` | ASCII；0x00 结尾；内容 + 1 ≤ 容量 |
| `S7String` | `string` | `Capacity + 2` | `B`；`DBS` + `.容量` | ASCII；首字节容量、第二字节长度 |
| `S7WString` | `string` | `4 + 2 × Capacity` | `B`；`DBL` + `.容量` | UTF-16BE；2 字节容量 + 2 字节长度（码元）；容量 1～16383；无终止符/BOM |

不支持：DateTime、嵌套结构体/UDT、数组与符号寻址；扁平结构体请使用下方的结构体读写。

### 地址表达式

- `DB1.DBB0`、`DB1.DBW0`、`DB1.DBD0`、`DB1.DBX0.3`
- `DB1.DBR13.15`（String，容量 15）、`DB1.DBS13.15`（S7String，容量 15）、`DB1.DBL13.15`（S7WString，容量 15）
- `DBR`/`DBS`/`DBL` 仅支持 `DBn.` 前缀，后缀只限定允许的数据类型；`DataType` 仍必须显式提供，不一致返回 `133`
- 地址容量与 `Capacity` 同时提供时，`Capacity=0` 取地址容量，`Capacity>0` 必须与地址容量相等，否则 `136`
- `IB0`/`IW10`/`ID4`、`I0.3`（`E` 同义）
- `QB0`/`QW0`/`QD0`、`Q0.3`（`A`/`O` 同义）
- `MB10`/`MW10`/`MD10`、`M10.3`
- 大小写不敏感；不支持 `T`/`C`/`Z` 与符号名；非法地址在本地拒绝且不发送报文

### 字符串编码

- `String`/`S7String`：固定 ASCII；写入非 ASCII → `StringEncodingInvalid`(135)；读回非 ASCII → `NonAsciiStringData`(407)
- `String` 容量 1～254，内容长度 + 1 ≤ 容量
- `S7String` 容量 1～254，占用容量 + 2 字节
- `S7WString`：严格 UTF-16BE 逐码元；容量 1～16383 码元（`string.Length`），占用 `4 + 2 × 容量`；头部容量可小于等于请求容量；孤立代理项写入 → 135、读回 → 409；头部非法 → 408

### 类型化错误码

| 错误码 | 数值 | 含义 |
|-------|------|------|
| `AddressSyntaxInvalid` | 130 | 地址语法非法 |
| `BitNumberOutOfRange` | 131 | 位号越界或位号位置非法 |
| `DataTypeNotSupported` | 132 | 类型不在十一种之内 |
| `DataTypeAddressMismatch` | 133 | 类型与访问宽度不兼容 |
| `ValueOutOfRange` | 134 | 写入值超出目标范围 |
| `StringEncodingInvalid` | 135 | 写入字符串编码非法（非 ASCII 或孤立代理项） |
| `StringCapacityInvalid` | 136 | 容量非法或内容超容量 |
| `AddressOutOfRange` | 137 | DB 号/偏移/占用越界 |
| `ValueTypeMismatch` | 138 | 值种类与目标类型不符 |
| `InvalidS7StringHeader` | 406 | 读回 S7 String 头部非法 |
| `NonAsciiStringData` | 407 | 读回字符串非 ASCII |
| `InvalidS7WStringHeader` | 408 | S7WString 头部非法（不复用 406/407） |
| `InvalidS7WStringData` | 409 | S7WString 字符数据非法（不复用 406/407） |

### 位写

- 单请求位写：36 字节帧，参数区传输尺寸 `0x01`、数据区 `0x03`，1 次网络往返
- PLC 原子置位/复位，只改变目标位；不采用“先读后写”
- 虚拟 PLC `127.0.0.1:102` 已实测支持（见验证记录 D-07）

## 错误处理

所有通讯失败统一抛出 `S7ClientException`：

```csharp
try
{
    await client.ReadBytesAsync(request, cancellationToken);
}
catch (S7ClientException ex)
{
    switch (ex.Category)
    {
        case S7ErrorCategory.PlcReturn:
            Console.WriteLine($"PLC 返回码 0x{ex.PlcReturnCode:X2}, 错误码 {ex.ErrorCode}");
            break;
        case S7ErrorCategory.Timeout:
        case S7ErrorCategory.Cancelled:
        case S7ErrorCategory.InvalidArgument:
        case S7ErrorCategory.Connection:
        case S7ErrorCategory.Protocol:
            Console.WriteLine($"{ex.Category} / {ex.ErrorCode} / {ex.Stage}: {ex.Message}");
            break;
    }
}
```

写入失败时读取进度：

```csharp
try
{
    await client.WriteBytesAsync(writeRequest);
}
catch (S7ClientException ex)
{
    Console.WriteLine($"已确认写入 {ex.BytesWritten} 字节，区间 {ex.WrittenRange}");
    if (ex.WriteOutcomeUncertain)
    {
        Console.WriteLine("写入结果不确定，建议重新读取确认现场数据");
    }
}
```

## 会话与并发规则

- 同一会话同一时刻只允许一个在途请求，后续请求按 FIFO 排队
- 排队深度由 `MaxQueuedRequests` 限制（默认 32），超出返回 `QueueOverflow`
- 已连接时重复连接返回 `SessionAlreadyConnected`
- `DisconnectAsync` 幂等；`Faulted` 状态下读写返回 `SessionFaulted`，需显式断开后重连
- 断线自动重连默认开启；显式设置 `AutoReconnectEnabled = false` 可退回基线。仅连接类故障触发，显式连接失败与读写超时均不触发；重连期间读写立即返回 `SessionReconnecting`(204)，写入永不重放

## 批量读写

一次批量调用只使用一个请求帧与一个响应帧，全部条目在发送前完成校验。条目数由当前协商 PDU 容量与协议 1 字节计数字段（255）共同决定；本地不做自动拆批、分组或截断，超限时整批拒绝，由调用方按容量自行分组。

字节视图：

```csharp
S7BatchReadResult bytes = await client.ReadBatchAsync(
[
    new S7BatchReadItem(S7Area.DataBlock, 1, 0, 32),
    new S7BatchReadItem(S7Area.Memory, 0, 0, 16),
    new S7BatchReadItem(S7Area.Output, 0, 0, 8)
]);

foreach (S7BatchReadItemResult item in bytes.Items)
{
    Console.WriteLine($"#{item.Index} ok={item.Succeeded} bytes={item.Data.Length}");
}

foreach (S7BatchItemFailure failure in bytes.Failures)
{
    string plcCode = failure.PlcReturnCode is byte code ? code.ToString("X2") : "-";
    Console.WriteLine($"失败 #{failure.Index} {failure.ErrorCode} PLC={plcCode}");
}

// 需要“全成功否则抛 144”时使用便利出口
bytes.EnsureAllSucceeded();
```

类型化视图：

```csharp
S7TypedBatchReadResult values = await client.ReadTypedBatchAsync(
[
    new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int),
    new S7TypedReadBatchItem("DB1.DBD4", S7DataType.Real),
    new S7TypedReadBatchItem("DB1.DBX8.3", S7DataType.Bool),
    new S7TypedReadBatchItem("DB1.DBB200", S7DataType.S7String, Capacity: 16)
]);

short speed = values.Items[0].Value.AsInt16();
string name = values.Items[3].Value.AsString();

S7TypedBatchWriteResult written = await client.WriteTypedBatchAsync(
[
    new S7TypedWriteBatchItem("DB2.DBW0", S7DataType.Int, S7Value.FromInt16(1500)),
    new S7TypedWriteBatchItem("DB2.DBX4.2", S7DataType.Bool, S7Value.FromBool(true))
]);
```

容量口径（均为 S7 净荷字节，不含 TPKT 4 + COTP 3 头）：

| 方向 | 所需净荷 |
|------|----------|
| 读请求 | `12 + 12N` |
| 读响应 | `14 + Σ(4 + A(D))`（每项结果头 4 字节） |
| 写请求 | `12 + Σ(16 + A(D))` |
| 写响应 | `14 + N`（恒小于写请求侧，无需单独估算） |

`A(D) = D + (D & 1)` 为偶数对齐。条目数为 0 返回 `BatchItemsEmpty`(141)；超过 255 条返回 `BatchItemCountExceeded`(146)；所需净荷超出当前 PDU 返回 `BatchPduExceeded`(142)。上述拒绝都发生在发送前，零报文、零写入。

按 PDU 自行分组示例：

```csharp
const int pduBudget = 430; // 保守预留后可用于批量读响应的净荷
var group = new List<S7BatchReadItem>();
int required = 14;

foreach (S7BatchReadItem item in items)
{
    int itemCost = 4 + item.Length + (item.Length & 1);
    if (group.Count > 0 && required + itemCost > pduBudget)
    {
        await client.ReadBatchAsync(group);
        group.Clear();
        required = 14;
    }

    group.Add(item);
    required += itemCost;
}

if (group.Count > 0)
{
    await client.ReadBatchAsync(group);
}
```

- 部分失败默认不抛异常：逐条返回 `Succeeded` / `ErrorCode` / `PlcReturnCode`，整批 `Failures` 按索引升序；`EnsureAllSucceeded()` 仅在需要「全成功」语义时抛 `BatchItemFailed`(144)，并携带失败明细、`SucceededItemCount` 与已写字节。
- 批量写条目目标字节区间不得重叠（含两个位条目落在同一字节）；冲突返回 `BatchAddressConflict`(143)，首尾相接不视为冲突。读条目允许重复或重叠，逐条独立返回。
- 位条目写入的已写字节按 1 计；批量不做回读校验。批量结果仅包含逐条数据、失败明细与实际写入字节数，不携带重连元数据。

## 结构体读写

结构体读写只映射公共实例字段，按声明顺序连续排布，不使用运行时对齐填充。字段类型限定为九种；`bool` 固定占 1 字节（读回非零即真）。

```csharp
public struct MotorData
{
    public bool Running;
    public short Speed;
    public int Counter;

    [S7StringField(16)]
    public string Name;

    [S7StringField(8, DataType = S7DataType.S7WString)]
    public string Label;
}

S7StructReadResult<MotorData> read = await client.ReadStructAsync<MotorData>("DB2.DBB0");
await client.WriteStructAsync("DB2.DBB0", new MotorData
{
    Running = true,
    Speed = 1500,
    Counter = 42,
    Name = "MAIN",
    Label = "主电机"
});
```

| C# 字段 | PLC 类型 | 占用字节 |
|---------|---------|---------|
| `bool` | `Bool` | 1（整字节，非零即真） |
| `byte` | `Byte` | 1 |
| `short` | `Int` | 2 |
| `ushort` | `Word` | 2 |
| `int` | `DInt` | 4 |
| `uint` | `DWord` | 4 |
| `float` | `Real` | 4 |
| `double` | `LReal` | 8 |
| `string` | `String` / `S7String` / `S7WString` | 容量 / 容量+2 / 4+2×容量 |

- 字符串字段必须使用 `[S7StringField(capacity)]`；默认编码 `S7String`，也可指定 `String` 或 `S7WString`。容量单位与单值类型化读写一致。
- 地址仅接受 `DBn.DBBm`；不支持的地址返回 `StructAddressNotSupported`(140)，区间越界返回 `AddressOutOfRange`(137)。
- 类型约束为 `where T : struct` + 运行时纯度检查：无引用等价 `unmanaged`；仅允许 `string` 引用字段，其他引用/嵌套/数组/枚举/`DateTime`/`decimal`/可空返回 `DataTypeNotSupported`(132)。该取舍用于同时支持 `string` 字段（字面 `unmanaged` 约束会禁止 `string`）。
- 布局非法返回 `StructLayoutInvalid`(139)：空布局、重复/错位标注、容量越界、总长越界、`LayoutKind.Explicit` 或 `FieldOffset`。
- `[StructLayout(LayoutKind.Sequential/Auto, Pack=...)]` 允许存在，但不参与偏移与长度计算；`LayoutKind.Explicit` 被拒绝。
- 读取整段一次请求（分包由会话层完成），全有或全无；写入先完成全部字段校验与编码，失败零发送。写入进度语义与字节读写一致，结果不携带重连元数据。
- 日志默认输出结构体单行 JSON 值（字段顺序与声明一致，键为字段名）；关闭 `EnableValueTrace` 后只记录类型名、映射字段数、地址、长度与错误码。

## 断线自动重连

自动重连默认开启，业务层无需关心设备断线；只有需要旧行为时才显式关闭。仅已建立会话后的连接类故障触发，显式连接失败与读写超时均不触发。只有一个配置项：

```csharp
var options = new S7ConnectionOptions
{
    IpAddress = "192.168.0.10",
    CpuType = S7CpuType.S71200,
    Rack = 0,
    Slot = 1,
    AutoReconnectEnabled = true // 默认值；设为 false 退回旧基线
};
await using var client = new S7Client(options);
```

- 断线当次调用立即返回原错误（如 301），不自动重试；后台立即发起首次重连（首次延迟为 0），失败后固定按 `1s → 2s → 4s → 8s → 16s → 30s` 退避，无次数上限、无抖动。
- 重连期间读写立即返回 `SessionReconnecting`(204)，不等待、不排队；恢复后由调用方下一次读写自然继续。
- 写入永不重放；结果不确定时保留 `BytesWritten` / `WrittenRange` / `WriteOutcomeUncertain`。
- 读写超时（601/602）不触发重连、不关闭连接、不改变会话状态；超时后保持已连接并复用同一连接。
- 读写超时与调用方取消在返回前同步排空迟到响应：排空成功时返回原错误码（601/602/603）并保持 `Connected`、复用原连接；排空失败时本次调用返回 `SessionFaulted`(202) 且会话转 `Faulted`，旧连接后台关闭、不触发重连，需显式断开后重连。
- 失败返回最多额外等待 `W = min(本阶段配置超时, 2 秒)`；单次调用总耗时上界为「配置超时 + W」。
- 失败日志的级别判定与条目构造全部在后台副作用泵执行；日志设施即使阻塞 `IsEnabled` 也不影响失败返回时延。
- 高频批量读写期间后台重连由专用线程启动，首轮尝试不受线程池排队影响，固定退避节奏不变。
- 重连成功后会话标识与协商 PDU 更新；快照仅包含 `State` / `SessionId` / `NegotiatedPduSize` / `AutoReconnectEnabled`，不提供专用重连事件或结果恢复元数据。
- 重连期间显式 `ConnectAsync` 返回 `SessionAlreadyConnected`；`DisconnectAsync` / `DisposeAsync` 会在 1 秒内中止重连，终态为 `Disconnected`。
- 建议注入 `transportFactory`（每次重连获取新传输实例）；注入单实例 `transport` 的组合同样可用于内存假传输，但不适用于 `TcpTransport` 释放后不可复用的场景。

## 日志与协议跟踪

```csharp
IS7Logger logger = new S7LoggerAdapter(loggerFactory.CreateLogger("S7"));
await using var client = new S7Client(options, logger);

// 仅调试时开启帧级十六进制输出
var tracedOptions = options with { EnableProtocolTrace = true };

// 需要彻底静默点位值时关闭值展示
var silentOptions = options with { EnableValueTrace = false };
```

默认日志以点位值为重点：`EnableValueTrace` 默认开启，字节读写输出大写无分隔十六进制，类型化读写输出 `类型=值`，结构体读写输出单行 JSON，批量读写输出 `[索引] 地址=值` 逐条列表。值文本上限为 Hex 256 字节、类型化 1024 字符、JSON 4096 字符、批量 32 条；超限会截断并标注。

- `EnableValueTrace = false` 后任何日志与渲染消息都不出现值内容；读写返回值、错误码与时序不变。
- `EnableProtocolTrace = true` 时逐 TPKT 帧输出 `TX` / `RX` 完整十六进制报文（不截断）并附带帧字节数；关闭时不产生 Trace 条目，也不做报文十六进制转换。两个开关相互独立。
- 日志条数不做唯一性约束：类型化、结构体、批量内部复用字节读写时，会按实际读写产生多条含值日志。
- 示例：`S7 Read stage=Read addr=DB1.DBW0 type=Int value=Int=123 result=OK elapsed=1.2ms`。
- 日志实现抛出异常时会被隔离，不影响主流程。

## 参数范围

| 参数 | 默认值 | 允许范围 |
|------|--------|---------|
| `Port` | 102 | 1 ~ 65535（虚拟 PLC 测试默认 1102） |
| `Rack` / `Slot` | 0 / 0 | 0 ~ 15 |
| `ConnectTimeout` | 5 s | 1 ~ 120 s |
| `ReadTimeout` / `WriteTimeout` | 10 s | 1 ~ 300 s |
| `RequestedPduSize` | 480 | 240 ~ 960（协商结果钳制在 `[240, 申请值]`） |
| `MaxQueuedRequests` | 32 | 1 ~ 1024 |
| 读取/写入长度 | — | 1 ~ 65535 字节 |
| `StartByte` | — | 0 ~ 65535 |
| DB 号 | — | DB 区 1 ~ 65535；其他区域必须为 0 |

## 构建与测试

```bash
# 四框架构建（net8.0 / net9.0 / net10.0 / net11.0）
dotnet build S7.NetCore.sln -c Release

# 仅 net10.0 执行全量测试；其它框架只构建不测试
dotnet test S7.NetCore.sln -c Release --no-build -f net10.0

# 单框架 RID 交叉构建示例
dotnet build src/S7.NetCore/S7.NetCore.csproj -c Release -f net10.0 -r win-x64 --no-self-contained
dotnet build src/S7.NetCore/S7.NetCore.csproj -c Release -f net10.0 -r linux-x64 --no-self-contained
dotnet build tests/S7.NetCore.Tests.Native/S7.NetCore.Tests.Native.csproj -c Release -f net10.0 -r linux-x64 --no-self-contained
dotnet build tests/S7.NetCore.Tests/S7.NetCore.Tests.csproj -c Release -f net10.0 -r linux-x64 --no-self-contained

# 校验四框架产物与 NuGet 包
pwsh -File tools/build/verify-frameworks.ps1 -Mode Output -Configuration Release
pwsh -File tools/build/verify-frameworks.ps1 -Mode Package -PackagePath artifacts/packages/SiemensS7.NetCore.6.0.0.nupkg
```

## 嵌入式虚拟 PLC（测试服务器夹具）

真实链路测试由测试进程内的原生 Snap7 `S7Server`（仅经 `snap7.net.cs` 托管）创建虚拟 PLC，默认端点 **`127.0.0.1:1102`**；外部 `127.0.0.1:102` 实例不再参与测试（生产客户端默认端口仍为 102）。运行验证仅在 Windows 执行，Linux 不执行虚拟 S7Server 验证。

| 环境变量 | 默认值 | 说明 |
|---------|-------|------|
| `S7_VIRTUAL_PLC_SERVER` | `embedded` | `embedded` / `external` / `off` |
| `S7_VIRTUAL_PLC_HOST` | `127.0.0.1` | 嵌入式必须回环 |
| `S7_VIRTUAL_PLC_PORT` | `1102` | 1024～65535 且不得为 102 |
| `S7_VIRTUAL_PLC_CPU` | `S71200` | 客户端 CPU 型号 |
| `S7_VIRTUAL_PLC_RACK` | `0` | 0～15 |
| `S7_VIRTUAL_PLC_SLOT` | `1` | 0～15 |
| `S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK` | `false` | 非回环写用例开关 |
| `S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS` | `1` | 1～8 |

```bash
dotnet test --filter "Category=VirtualPlc"
dotnet test --filter "Category!=VirtualPlc"
dotnet test --filter "Category=Naming"
```

- 夹具启动：创建 S7Server → 设置端口/并发/CPU 状态 → 注册 PE/PA/MK/DB1～DB3 → 初始化确定性初值 → 绑定回环启动 → 自检 → 登记实测基线
- 停止：Stop → 注销区域 → 失效缓冲区 → `S7Server.Destroy()` → 端口释放核对；停止幂等，支持 3 轮重启
- 原生资产仅测试使用：`snap7.net.cs` 由 `tests/S7.NetCore.Tests.Native` 链接编译（不加入 `.sln`），测试项目仅复制 `snap7.dll`；虚拟 S7Server 只经 `snap7.net.cs` 的 `S7Server` 托管，测试项目不定义原生绑定，也不做库路径探测或平台判定；**生产程序集零原生依赖**
- 跳过仅限环境原因：`SERVER=off`、原生库缺失/坏映像/入口点缺失、端口非法/被占用、外部端点不可达、配置非法；区域注册、自检、断言、现场恢复、停止释放失败一律失败
- 68 项 VirtualPlc 用例（55 项迁移 + 10 项类型化新增 + 3 项结构体新增）默认连接夹具端点；`SERVER=off` 或库缺失时为 62 项跳过、6 项静态/连接失败用例通过、失败 0
- `S7Server` 原生接口的 `Size` 为 16 位，单区最大注册容量为 **65535** 字节（规格 65536 的偏差待评审）；`V_R04` 尾部读取调整为起始 65533 的 2 字节
- DB2 偏移段、DB1 只读与现场快照恢复约束保持不变

### 区域映射（D-01）

spec 5.5.1.8a 的“向 DB1 写 1024 字节”按 D-01 映射为 **DB2 起始 0**；DB1 仅承载读取、分包与越界用例，测试目录中不存在任何 DB1 写入调用。

## 验证记录

| 项目 | 结果 |
|------|------|
| 多框架构建（net8.0 / net9.0 / net10.0 / net11.0） | 四框架 Release 通过，0 警告 0 错误；net10.0 为完整测试验证，其余为仅构建验证 |
| net10.0 全量测试 | 625 项中 624 通过；1 项为任务外既有 `PackageTags` 命名检查失败（`N17_ProductionHasNoNativeDependency`）；本次改动相关用例全部通过 |
| 结构体读写用例 | 覆盖契约/布局/地址/编解码/读写路径/零发送/跨 PDU/日志，以及 3 项 VirtualPlc（数值回环、字符串三编码回环、PLC 错误映射） |
| 自动重连用例 | 32 项（含参数化）：默认开关与公开契约、固定退避、断线返回原错误、后台立即首连、重连中 204/603、写入不重放、快照、1 秒内中止、201、日志、超时保持连接；5.1.0 新增 15 项快速失败时序用例，覆盖订阅方/日志/传输关闭阻塞、迁移单次、排队终结、分包读/写中途、批量零发送、取消优先、通知顺序、释放终止；5.2.0 新增 12 项超时/取消即时返回、延后善后与 202、日志 `IsEnabled` 阻塞隔离、批量超时即时返回用例；6.0.0 将延后善后回退为当次同步排空，新增 202 覆盖、写入进度保留、取消排空失败、并发断开优先、类型错配拒绝、不重连与发送前零预排空 7 项红线用例；同时移除批量/类型化/结构体结果的重连元数据断言 |
| `dotnet build`（Debug / Release，warnings-as-errors） | 通过，0 警告 0 错误 |
| 版本 6.0.0 | `VersionPrefix` = `6.0.0`，包版本、程序集版本、文件版本与信息版本均由该单一来源派生；包标识仍为 `SiemensS7.NetCore`，包文件为 `SiemensS7.NetCore.6.0.0.nupkg` / `.snupkg` |
| 批量读写用例 | 覆盖容量边界、写重叠、请求校验、多条目协议编解码、结果装配、字节/类型化门面端到端、超时/取消/断线原错返回与重连后继续 |
| 版本引用点同步 | `Directory.Build.props`、`tools/naming/inspect.ps1`（从 `VersionPrefix` 派生）、命名测试（从 `Directory.Build.props` 派生）、`tools/build/verify-frameworks.ps1`、`tools/publish/publish-nuget.ps1` 与 README 已同步为 `6.0.0` |
| 协议与假传输测试 | 244 项全部通过（`FakeTcpTransport` 驱动） |
| VirtualPlc 用例 | Windows x64 上 68/68 通过（55 项迁移 + 10 项类型化新增 + 3 项结构体新增，嵌入式 S7Server，端点 `127.0.0.1:1102`） |
| Linux 虚拟 S7Server 验证 | **未执行**；Linux 仅做构建与非虚拟服务器用例，不含虚拟服务器运行结论 |
| 关闭/无库跳过语义 | `SERVER=off` 与 `snap7.dll` 缺失：62 项跳过、6 项通过、0 失败；原因分别为 ModeOff 与 LibraryMissing |
| 端口占用语义 | 预占用 `127.0.0.1:1102`：62 项跳过、6 项通过、0 失败 |
| 嵌入式实测基线 | PDU=480；R=462；W=445；启动 35.6ms；6 个区域 × 65535 字节 |
| 生命周期与数据 | 3 轮停止/重启成功、端口可重绑；停止幂等；区域首字节均为 `0x0B` 且重启后一致 |
| 命名检查 | `Category=Naming` 20 项 PASS；`tools/naming/inspect.ps1` 22 项 PASS（N-01～N-22） |
| 回环性能 V-P01（1024 字节读，迁移前外部 102） | P95=1.5ms（min 0.7 / p50 1.1 / max 1.6） |
| 回环性能 V-P02（连接+断开，迁移前外部 102） | P95=1.1ms（min 0.4 / p50 0.6 / max 5.7） |
| 回环性能 T-P01（类型化 Int 读，迁移前外部 102） | P95=0.2ms（min 0.1 / p50 0.2 / max 0.2） |
| 回环性能 T-P02（S7WString 读，迁移前外部 102） | P95=0.3ms（min 0.2 / p50 0.2 / max 0.3） |
| S7WString 字节布局/回环 | `温度A1` 头 `00 10 00 04`、UTF-16BE `6E 29 5E A6 00 41 00 31`；`😀` = `D8 3D DE 00`；空串声明长度 0；408/409 不返回部分值 |
| D-07 位写 | 嵌入式 S7Server 支持单请求位写：1 帧 36 字节、`frame[22]=0x01`、`frame[32]=0x03`，仅目标位变化，1 次往返 |
| 报文金标准 | COTP CR、S7 Setup、读/写请求与 `s7netplus` 逐字节一致；位写 4 组样本逐字节一致 |
| 跨平台构建 | `win-x64`、`linux-x64` 项目级交叉构建通过；仅构建通过，不含虚拟 S7Server 运行验证；解决方案级 RID 构建受 SDK `NETSDK1134` 限制 |
| 原生依赖检查 | 生产零原生：无 `DllImport`/`LibraryImport`/`NativeLibrary`；桥接项目仅链接 `snap7.net.cs`，测试项目复制 `snap7.dll` |
| 容量偏差 V-07 | S7Server 的 `word Size` 上限 65535；规格 65536 未满足，`V_R04` 起点改为 65533；**待评审** |
| 真实 PLC 多型号冒烟（S7-1200/1500/300/400/200Smart） | **未执行**（缺多型号设备） |
| 局域网真实 PLC P95 | **未测量**（当前仅本机回环基线） |
| 规格偏差 T-E02 | spec 5.9.1.17b 期望 PLC `0x05`；`DB1.DBD65534` Real 占用 4 字节超出区域容量，按 design 23.3/23.6 本地返回 137 且零发送；**待评审** |
| 重命名 2.0.0（2026-11-21） | 313 项测试（历史基线）；Release 双平台 0 警告；N-01～N-15 PASS |

## 后续扩展点

- DateTime、嵌套结构体/UDT、数组与嵌套类型
- 符号寻址与变量表导入
- Timer / Counter 区域
- PLC 时钟、SZL 与块操作
- 连接池与多会话并发

## 变更记录与迁移

### 迁移说明（旧写法 → 当前写法）

- 泛型取值：旧写法 `As<T>()` 改为 `ToValue<T>()`；`TryToValue<T>(out T)` 与 `ToValue<T>()` 并存，不保留别名。`ToValue<long>` / `ToValue<ulong>` 受支持，可空目标（如 `ToValue<int?>`）仍不支持；九个强类型访问器（`AsBoolean`、`AsByte`、`AsInt16`、`AsUInt16`、`AsInt32`、`AsUInt32`、`AsSingle`、`AsDouble`、`AsString`）保持不变。
- 字符串判定：`DataType == S7DataType.String` 改为 `IsStringValue`；需要区分 `String` / `S7String` / `S7WString` 时使用 `StringEncoding`。
- 批量读写为新增 API，不改动既有单点读写的公开签名与错误语义。
- 自动重连 5.0.0 迁移：
  - 删除 `S7ConnectionOptions.Reconnect` 与 `S7ReconnectPolicy`，改为单一布尔开关 `AutoReconnectEnabled`（默认 `true`）；需要旧行为时显式设为 `false`。
  - 删除 `S7ReadResult` / `S7WriteResult` 及 `ReadBytesResultAsync` / `WriteBytesResultAsync` / `WriteValueResultAsync` / `WriteStructResultAsync`；字节读写改用 `ReadBytesAsync` / `WriteBytesAsync`，类型化与结构体分别使用 `ReadValueAsync` / `WriteValueAsync` / `ReadStructAsync` / `WriteStructAsync`。
  - 所有结果类型移除 `RecoveryCount` / `Retried` / `ReplayAttempted` / `ReplayedBytes`；删除 `ReconnectEvent` 与 `S7ReconnectEventArgs`；`SessionSnapshot` 仅保留 `State` / `SessionId` / `NegotiatedPduSize` / `AutoReconnectEnabled`。
  - 默认开启后，断线会短暂进入 `Reconnecting`，期间读写返回 204；断线当次调用仍返回原错误。`106` / `604` / `605` 保留数值但不再产生。
  - 读写超时（601/602）后保持 `Connected` 并复用连接，不触发重连、不关闭连接、不置 `Faulted`。
- 自动重连 5.2.0 时序强化（已被 6.0.0 取代）：
  - 5.2.0 曾将超时/取消后的迟到响应排空延后到下一次调用；6.0.0 已撤回该机制，恢复为失败调用返回前同步排空。
  - 保留项：失败日志的 `IsEnabled` 判定与条目构造仍在后台副作用泵执行；后台重连仍使用专用线程启动，高频批量读写下首轮尝试不受线程池排队影响。
  - 公开 API、错误码、会话状态集合、快照/日志字段以及 601/602「排空成功时保持已连接并复用连接」语义均不变。
- 自动重连 6.0.0 迁移：
  - 超时/取消调用在返回前同步排空迟到响应；失败返回最多额外等待 `W = min(配置超时, 2 秒)`，单次调用总耗时上界为「配置超时 + W」，撤回 5.2.0「5ms 内返回」的时序承诺。
  - 排空成功：返回原错误码（601/602/603），会话保持 `Connected` 并复用原连接。
  - 排空失败：本次调用返回 `SessionFaulted`(202)，会话转 `Faulted`，旧连接后台关闭且不启动重连；调用方须显式断开并重新连接。
  - 排空期间显式断开/释放：排空被中止，调用返回原错误码而非 202，释放不超过 1 秒。
- 自动重连 5.1.0 时序强化：
  - 断线当次调用在故障判定后立即返回原错误，不等待旧连接关闭、状态通知派发、后台重连启动或失败日志写出。
  - 重连期间任意读写（含排队中与分包/批量中途）立即返回 204；分包读在下一帧发送前中止并丢弃已读分片，分包写返回 204 时携带已确认写入进度与结果不确定标记。
  - 批量请求在发送前转入重连中时零发送返回 204；取消优先返回 603。
  - 释放/断开可终结排队与分包中途请求（≤1 秒），不挂起。
  - 公开 API、错误码、会话状态集合、快照/日志字段与 601/602 超时语义均不变。

| 版本 | 日期 | 变更 |
|------|------|------|
| 6.0.0 | 2026-11-27 | 迟到响应排空策略回退为「当次请求内同步排空」：删除 `AftercareQueue` / `PostAftercare` / `DrainPendingAsync` 等延后善后机制，5 个失败出口在返回前同步排空；排空成功返回原错误码（601/602/603）并保持 `Connected`，排空失败返回 `SessionFaulted`(202) 并转 `Faulted`、不触发重连；失败返回最多额外等待 `W = min(配置超时, 2 秒)`，撤回 5.2.0「5ms 内返回」承诺；排空命中判据补报文功能码校验；新增会话级排空中止；公开 API/错误码/状态集合/快照与日志字段不变；版本提升至 6.0.0 |
| 5.2.0 | 2026-11-26 | 单条读写失败即返回时序强化：超时（601/602）与取消（603）在判定后立即返回，不再在返回前等待最长 2 秒的迟到响应排空或同步关闭连接；迟到响应改由下一次取得会话许可的调用在发送前清理，清理失败返回 `SessionFaulted`(202) 且不触发重连；状态迁移通知的 `EventArgs`/时间戳构造移出锁内返回路径；失败日志 `IsEnabled` 与条目构造完全后台化；后台重连改用专用线程启动，高频批量不推迟首轮尝试；公开 API/错误码/状态集合/快照与日志字段不变；版本提升至 5.2.0 |
| 5.1.0 | 2026-11-25 | 断线快速失败时序强化：断线当次调用立即返回原错误（不等待旧连接关闭/通知派发/后台启动/失败日志写出）；重连期间读写立即 204（含排队中与分包/批量中途），中途读丢弃分片、中途写携带已确认进度与不确定标记；释放/断开可终结排队与中途请求（≤1 秒）；公开 API/错误码/状态集合/快照与日志字段/601-602 超时语义不变；失败日志改为后台串行写出，成功/信息/调试/重连日志保持同步；版本提升至 5.1.0 |
| 5.0.0 | 2026-11-25 | 简化断线自动重连：只保留 `S7ConnectionOptions.AutoReconnectEnabled` 一个开关并默认开启；断线当次调用立即返回原错误、后台立即首次重连（首次延迟 0），失败后固定 `1s → 2s → 4s → 8s → 16s → 30s` 退避，无次数上限；重连期间读写立即返回 204、不等待；写入永不重放；删除 `S7ReconnectPolicy` 的 8 项策略、`ReconnectEvent`、`S7ReadResult`/`S7WriteResult` 及所有结果中的重连元数据；快照精简为 4 个字段；`106`/`604`/`605` 保留数值不再产生；读写超时后保持已连接并复用连接；主版本升至 5.0.0 |
| 4.1.0 | 2026-11-24 | 新增字符串容量后缀地址：`DB1.DBR13.15`（String）、`DB1.DBS13.15`（S7String）、`DB1.DBL13.15`（S7WString）；后缀仅限定允许类型，`DataType` 仍必须显式提供，缺失或不一致返回 133；地址容量与 `Capacity` 同时提供时，0 取地址容量、非 0 必须相等，否则 136；仅 `DBn.` 前缀支持；`DB1.DBB13.15` 仍按 131 拒绝，结构体新后缀按 140 拒绝；删除 `S7.NetCore.Tools` 工具项目（解决方案 3→2）；版本提升至 4.1.0 |
| 4.0.0 | 2026-11-24 | 新增公开批量读写 API：字节视图 `ReadBatchAsync`/`WriteBatchAsync` 与类型化视图 `ReadTypedBatchAsync`/`WriteTypedBatchAsync`，一次批量调用 = 1 个请求帧 + 1 个响应帧；条目数由当前 PDU 与协议 255 计数字段决定，超限整批拒绝并由调用方分组（PDU 超限 142、条目数超限 146）；部分失败默认不抛异常，`EnsureAllSucceeded()` 为可选出口；批量写条目目标区间重叠禁止（143）；新增 `S7BatchReadItem`/`S7BatchWriteItem`/`S7TypedReadBatchItem`/`S7TypedWriteBatchItem` 与对应结果类型、`S7BatchItemFailure`、错误码 141～146；除已声明的 `As<T>()` → `ToValue<T>` 迁移与字符串标注精确化外，既有 API 不变；单点读取内部实现（含 `S7Value` 承载）允许变更但对外契约不变；字符串结果标注精确化为 `String`/`S7String`/`S7WString`（`StringEncoding`），`S7Value` 承载扩展为已声明行为变更；主版本升级对应上述新增与行为变更 |
| 3.0.0 | 2026-11-22 | 版本号跨主版本提升至 3.0.0（主版本 2 → 3）；包版本与程序集版本同步为 3.0.0；NuGet 包标识经用户确认回退为 `SiemensS7.NetCore`（第一轮 `Siemens.S7.NetCore` 停止使用，不再发布新版本），包文件为 `SiemensS7.NetCore.3.0.0.nupkg` / `.snupkg`；项目名称、程序集名与根命名空间保持不变 |
| 2.1.0 | 2026-11-21 | 增加 net8.0 / net9.0 / net10.0 / net11.0 四框架支持；net10.0 为唯一全量测试框架，其余仅构建；依赖按框架主版本分组；包版本提升至 2.1.0；新增 S7Client 断线自动重连（默认关闭，含退避、等待与恢复元数据）；新增扁平 C# 结构体读写（`ReadStructAsync`/`WriteStructAsync`/`WriteStructResultAsync`、`S7StringFieldAttribute`、错误码 139/140）；新增 `S7.NetCore.Tools` WPF 调试工具首版（连接/字节/类型化/日志/自动重连 UI，`net10.0-windows`） |
| 2.0.0 | 2026-11-21 | 项目、程序集与根命名空间由 `Snap7.NetCore` 统一重命名为 `S7.NetCore`；解决方案、测试项目与工具脚本同步更新，生产程序集保持零原生依赖 |
