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
- 超 PDU 自动分包：读取按 `PDU − 18`、写入按 `PDU − 35` 拆分
- 读取全有或全无；写入失败/取消可精确上报已写字节数与区间
- 取消不破坏会话（迟到响应清理）；连接/读/写超时独立可配
- 地址表达式解析：`DB1.DBW0`、`DB1.DBB0`、`DB1.DBD0`、`DB1.DBX0.3`、`MW10`、`Q0.3` 等
- 十一种类型转换：Bool、Byte、Word、Int、DWord、DInt、Real、LReal、String、S7String、S7WString
- 类型化读写与单请求位写（只改变目标位，1 次往返）
- 扁平结构体读写：公共实例字段按声明顺序连续映射，支持数值与带容量标注的字符串字段
- 统一错误模型（六类错误 + 稳定错误码 + PLC 原始返回码）
- 结构化诊断日志、可选帧级协议跟踪、日志失败隔离
- 断线自动重连（默认关闭；连接类故障触发，含退避、请求等待与结果恢复元数据）
- 纯托管实现，无 `DllImport`、无原生 Snap7 库、无第三方 S7 通讯库

**明确不做**

- DateTime、嵌套结构体/UDT、数组与嵌套类型
- 符号寻址与变量表导入
- 一次请求中的多变量批量读写
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
| `String` | `string` | `Capacity` | `B` | ASCII；0x00 结尾；内容 + 1 ≤ 容量 |
| `S7String` | `string` | `Capacity + 2` | `B` | ASCII；首字节容量、第二字节长度 |
| `S7WString` | `string` | `4 + 2 × Capacity` | `B` | UTF-16BE；2 字节容量 + 2 字节长度（码元）；容量 1～16383；无终止符/BOM |

不支持：DateTime、嵌套结构体/UDT、数组与符号寻址；扁平结构体请使用下方的结构体读写。

### 地址表达式

- `DB1.DBB0`、`DB1.DBW0`、`DB1.DBD0`、`DB1.DBX0.3`
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
- 默认不自动重连；启用 `Reconnect` 后按策略恢复。重连期间读写等待窗口到期返回 `SessionReconnecting`(204)，重连耗尽返回 `ReconnectExhausted`(604)，被显式断开或客户端释放中止返回 `ReconnectAborted`(605)

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
- 读取整段一次请求（分包由会话层完成），全有或全无；写入先完成全部字段校验与编码，失败零发送。重连与写入进度语义与字节读写一致。
- 日志只记录类型名、映射字段数、地址、长度、错误码，不记录任何字段值。

## 断线自动重连

自动重连默认关闭；通过 `S7ConnectionOptions.Reconnect` 显式启用。仅连接类故障触发（读写超时默认不触发），初次显式连接失败不触发。重连期间同一会话的读写请求在等待窗口内等待恢复；单次调用最多因重连恢复一次。

```csharp
var options = new S7ConnectionOptions
{
    IpAddress = "192.168.0.10",
    CpuType = S7CpuType.S71200,
    Rack = 0,
    Slot = 1,
    Reconnect = new S7ReconnectPolicy
    {
        AutoReconnectEnabled = true,
        MaxAttempts = 5,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        WaitTimeout = TimeSpan.FromSeconds(10)
    }
};
await using var client = new S7Client(options);
client.ReconnectEvent += (_, e) => Console.WriteLine($"{e.Kind} attempt={e.Attempt} error={e.ErrorCode}");
```

| 策略项 | 默认值 | 取值范围 / 说明 |
|--------|--------|----------------|
| `AutoReconnectEnabled` | `false` | 是否启用 |
| `MaxAttempts` | `5` | 0 至 100；0 表示不限 |
| `InitialDelay` | `1 s` | 100 ms 至 30 s |
| `MaxDelay` | `30 s` | 1 至 300 s，且不小于 `InitialDelay` |
| `BackoffMultiplier` | `2.0` | 1.0 至 10.0 |
| `JitterFactor` | `0.2` | 0 至 0.5 |
| `WaitTimeout` | `10 s` | 0 至 120 s；0 表示不等待 |
| `ReconnectOnTimeout` | `false` | 读写超时是否触发重连 |
| `ReplayUncertainWrites` | `false` | 结果不确定的写入是否允许重放 |

- 分包读取在断线后从头重读，不跨会话拼接；结果不确定的写入默认直接失败并保留 `BytesWritten` / `WrittenRange` / `WriteOutcomeUncertain`，开启 `ReplayUncertainWrites` 后从首个未确认分段起点继续。
- 重连成功后会话标识与协商 PDU 更新；`ReadBytesResultAsync` / `WriteBytesResultAsync` / `ReadValueAsync` 携带 `RecoveryCount`（0 或 1）与 `Retried`。
- 启用重连时应注入 `transportFactory`（每次重连获取新传输实例）；注入单实例 `transport` 且不提供工厂的组合同样能工作于内存假传输，但不适用于 `TcpTransport` 释放后不可复用的场景。
- 重连期间显式 `ConnectAsync` 返回 `SessionAlreadyConnected`；`DisconnectAsync` / `DisposeAsync` 会中止重连，终态为 `Disconnected`。

## 日志与协议跟踪

```csharp
IS7Logger logger = new S7LoggerAdapter(loggerFactory.CreateLogger("S7"));
await using var client = new S7Client(options, logger);

// 仅调试时开启帧级十六进制输出
var tracedOptions = options with { EnableProtocolTrace = true };
```

默认日志仅记录操作元数据（区域、DB 号、起始地址、长度、耗时、结果），不记录读写数据内容。日志实现抛出异常时会被隔离，不影响主流程。

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
pwsh -File tools/build/verify-frameworks.ps1 -Mode Package -PackagePath artifacts/packages/SiemensS7.NetCore.2.1.0.nupkg
```

## 嵌入式虚拟 PLC（测试服务器夹具）

真实链路测试由测试进程内的原生 Snap7 `S7Server` 创建虚拟 PLC，默认端点 **`127.0.0.1:1102`**；外部 `127.0.0.1:102` 实例不再参与测试（生产客户端默认端口仍为 102）。

| 环境变量 | 默认值 | 说明 |
|---------|-------|------|
| `S7_VIRTUAL_PLC_SERVER` | `embedded` | `embedded` / `external` / `off` |
| `S7_VIRTUAL_PLC_HOST` | `127.0.0.1` | 嵌入式必须回环 |
| `S7_VIRTUAL_PLC_PORT` | `1102` | 1024～65535 且不得为 102 |
| `S7_VIRTUAL_PLC_CPU` | `S71200` | 客户端 CPU 型号 |
| `S7_VIRTUAL_PLC_RACK` | `0` | 0～15 |
| `S7_VIRTUAL_PLC_SLOT` | `1` | 0～15 |
| `S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK` | `false` | 非回环写用例开关 |
| `S7_VIRTUAL_PLC_SERVER_LIBRARY` | 空 | 空 = 自动查找 `snap7.dll` / `libsnap7.so` |
| `S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS` | `1` | 1～8 |

```bash
dotnet test --filter "Category=VirtualPlc"
dotnet test --filter "Category!=VirtualPlc"
dotnet test --filter "Category=Naming"
```

- 夹具启动：创建 S7Server → 设置端口/并发/CPU 状态 → 注册 PE/PA/MK/DB1～DB3 → 初始化确定性初值 → 绑定回环启动 → 自检 → 登记实测基线
- 停止：Stop → 注销区域 → 失效缓冲区 → 显式 `Srv_Destroy` → 端口释放核对；停止幂等，支持 3 轮重启
- 原生资产仅测试使用：`snap7.net.cs` 由 `tests/S7.NetCore.Tests.Native` 链接编译（不加入 `.sln`），`snap7.dll` 复制到测试输出；**生产程序集零原生依赖**
- 跳过仅限环境原因：`SERVER=off`、原生库缺失/架构不匹配、平台不支持、端口非法/被占用、外部端点不可达；区域注册、自检、断言、现场恢复、停止释放失败一律失败
- 65 项 VirtualPlc 用例（55 项迁移 + 10 项新增）默认连接夹具端点；`SERVER=off` 或库缺失时为 59 项跳过、6 项静态/连接失败用例通过、失败 0
- `S7Server` 原生接口的 `Size` 为 16 位，单区最大注册容量为 **65535** 字节（规格 65536 的偏差待评审）；`V_R04` 尾部读取调整为起始 65533 的 2 字节
- DB2 偏移段、DB1 只读与现场快照恢复约束保持不变

### 区域映射（D-01）

spec 5.5.1.8a 的“向 DB1 写 1024 字节”按 D-01 映射为 **DB2 起始 0**；DB1 仅承载读取、分包与越界用例，测试目录中不存在任何 DB1 写入调用。

## 验证记录

| 项目 | 结果 |
|------|------|
| 多框架构建（net8.0 / net9.0 / net10.0 / net11.0） | 四框架 Release 通过，0 警告 0 错误；net10.0 为完整测试验证，其余为仅构建验证 |
| net10.0 全量测试 | 362 项通过，失败 0，跳过 0（`dotnet test ... --no-build -f net10.0`） |
| 结构体读写用例 | 新增 22 项：19 项单元（契约/布局/地址/编解码/读写路径/零发送/跨 PDU/日志/重连元数据）+ 3 项 VirtualPlc（数值回环、字符串三编码回环、PLC 错误映射） |
| 自动重连用例 | 新增 10 项：策略默认值与校验、退避序列、读取恢复、类型化恢复元数据、等待窗口 204、耗尽 604、写入默认不重放、快照、断开中止 605、事件顺序 |
| `dotnet build`（Debug / Release，warnings-as-errors） | 通过，0 警告 0 错误 |
| 全量测试 | 362 项全部通过（Release / net10.0） |
| 协议与假传输测试 | 244 项全部通过（`FakeTcpTransport` 驱动） |
| VirtualPlc 用例 | 68/68 通过（55 项迁移 + 10 项类型化新增 + 3 项结构体新增，嵌入式 S7Server，端点 `127.0.0.1:1102`） |
| 关闭/无库跳过语义 | `SERVER=off` 与无效库路径：59 项跳过、6 项通过、0 失败 |
| 嵌入式实测基线 | PDU=480；R=462；W=445；启动 35.6ms；6 个区域 × 65535 字节 |
| 生命周期与数据 | 3 轮停止/重启成功、端口可重绑；停止幂等；区域首字节均为 `0x0B` 且重启后一致 |
| 命名检查 | `Category=Naming` 19 项 PASS；`tools/naming/inspect.ps1` 20 项 PASS（N-01～N-20） |
| 回环性能 V-P01（1024 字节读，迁移前外部 102） | P95=1.5ms（min 0.7 / p50 1.1 / max 1.6） |
| 回环性能 V-P02（连接+断开，迁移前外部 102） | P95=1.1ms（min 0.4 / p50 0.6 / max 5.7） |
| 回环性能 T-P01（类型化 Int 读，迁移前外部 102） | P95=0.2ms（min 0.1 / p50 0.2 / max 0.2） |
| 回环性能 T-P02（S7WString 读，迁移前外部 102） | P95=0.3ms（min 0.2 / p50 0.2 / max 0.3） |
| S7WString 字节布局/回环 | `温度A1` 头 `00 10 00 04`、UTF-16BE `6E 29 5E A6 00 41 00 31`；`😀` = `D8 3D DE 00`；空串声明长度 0；408/409 不返回部分值 |
| D-07 位写 | 嵌入式 S7Server 支持单请求位写：1 帧 36 字节、`frame[22]=0x01`、`frame[32]=0x03`，仅目标位变化，1 次往返 |
| 报文金标准 | COTP CR、S7 Setup、读/写请求与 `s7netplus` 逐字节一致；位写 4 组样本逐字节一致 |
| 跨平台构建 | `win-x64`、`linux-x64` 项目级交叉构建通过；解决方案级 RID 构建受 SDK `NETSDK1134` 限制 |
| 原生依赖检查 | 生产零原生：无 `DllImport`/`LibraryImport`/`NativeLibrary`；测试桥接项目链接 `snap7.net.cs` 并复制 `snap7.dll` |
| 容量偏差 V-07 | S7Server 的 `word Size` 上限 65535；规格 65536 未满足，`V_R04` 起点改为 65533；**待评审** |
| 真实 PLC 多型号冒烟（S7-1200/1500/300/400/200Smart） | **未执行**（缺多型号设备） |
| 局域网真实 PLC P95 | **未测量**（当前仅本机回环基线） |
| 规格偏差 T-E02 | spec 5.9.1.17b 期望 PLC `0x05`；`DB1.DBD65534` Real 占用 4 字节超出区域容量，按 design 23.3/23.6 本地返回 137 且零发送；**待评审** |
| 重命名 2.0.0（2026-11-21） | 313 项测试（历史基线）；Release 双平台 0 警告；N-01～N-15 PASS |

## 后续扩展点

- DateTime、嵌套结构体/UDT、数组与嵌套类型
- 符号寻址与变量表导入
- 多变量打包读写
- Timer / Counter 区域
- PLC 时钟、SZL 与块操作
- 连接池与多会话并发

## 变更记录与迁移

| 版本 | 日期 | 变更 |
|------|------|------|
| 2.1.0 | 2026-11-21 | 增加 net8.0 / net9.0 / net10.0 / net11.0 四框架支持；net10.0 为唯一全量测试框架，其余仅构建；依赖按框架主版本分组；包版本提升至 2.1.0；新增 S7Client 断线自动重连（默认关闭，含退避、等待与恢复元数据）；新增扁平 C# 结构体读写（`ReadStructAsync`/`WriteStructAsync`/`WriteStructResultAsync`、`S7StringFieldAttribute`、错误码 139/140） |
| 2.0.0 | 2026-11-21 | 项目、程序集与根命名空间由 `Snap7.NetCore` 统一重命名为 `S7.NetCore`；解决方案、测试项目与工具脚本同步更新，生产程序集保持零原生依赖 |
