using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools;

public static class Texts
{
    public const string WindowTitle = "S7.NetCore 调试工具";

    public static string Describe(S7SessionState state) => state switch
    {
        S7SessionState.Disconnected => "未连接",
        S7SessionState.Connecting => "连接中",
        S7SessionState.Connected => "已连接",
        S7SessionState.Faulted => "异常",
        S7SessionState.Reconnecting => "重连中",
        _ => state.ToString()
    };

    public static string Describe(S7Area area) => area switch
    {
        S7Area.Input => "输入区 I",
        S7Area.Output => "输出区 Q",
        S7Area.Memory => "位存储区 M",
        S7Area.DataBlock => "数据块 DB",
        _ => area.ToString()
    };

    public static string Describe(S7DataType dataType) => dataType switch
    {
        S7DataType.Bool => "Bool",
        S7DataType.Byte => "Byte",
        S7DataType.Word => "Word",
        S7DataType.Int => "Int",
        S7DataType.DWord => "DWord",
        S7DataType.DInt => "DInt",
        S7DataType.Real => "Real",
        S7DataType.LReal => "LReal",
        S7DataType.String => "String",
        S7DataType.S7String => "S7String",
        S7DataType.S7WString => "S7WString",
        _ => dataType.ToString()
    };

    public static string Describe(S7CpuType cpuType) => cpuType switch
    {
        S7CpuType.S7200 => "S7-200",
        S7CpuType.Logo0BA8 => "Logo 0BA8",
        S7CpuType.S7200Smart => "S7-200 Smart",
        S7CpuType.S7300 => "S7-300",
        S7CpuType.S7400 => "S7-400",
        S7CpuType.S71200 => "S7-1200",
        S7CpuType.S71500 => "S7-1500",
        _ => cpuType.ToString()
    };

    public static string Describe(S7ErrorCategory category) => category switch
    {
        S7ErrorCategory.InvalidArgument => "参数或状态非法",
        S7ErrorCategory.Connection => "连接错误",
        S7ErrorCategory.Protocol => "协议错误",
        S7ErrorCategory.PlcReturn => "PLC 返回错误",
        S7ErrorCategory.Timeout => "操作超时",
        S7ErrorCategory.Cancelled => "操作取消",
        _ => category.ToString()
    };

    public static string Describe(S7OperationStage stage) => stage switch
    {
        S7OperationStage.Validate => "本地校验",
        S7OperationStage.Connect => "TCP 连接",
        S7OperationStage.CotpConnect => "COTP 连接",
        S7OperationStage.S7Setup => "S7 通讯设置",
        S7OperationStage.Read => "读取",
        S7OperationStage.Write => "写入",
        S7OperationStage.Disconnect => "断开",
        S7OperationStage.Reconnect => "自动重连",
        _ => stage.ToString()
    };

    public static string Describe(S7LogLevel level) => level switch
    {
        S7LogLevel.Trace => "跟踪",
        S7LogLevel.Debug => "调试",
        S7LogLevel.Information => "信息",
        S7LogLevel.Warning => "警告",
        S7LogLevel.Error => "错误",
        _ => level.ToString()
    };

    public static string Describe(OperationCategory category) => category switch
    {
        OperationCategory.Connect => "连接",
        OperationCategory.Disconnect => "断开",
        OperationCategory.ByteRead => "字节读取",
        OperationCategory.ByteWrite => "字节写入",
        OperationCategory.TypedRead => "类型化读取",
        OperationCategory.TypedWrite => "类型化写入",
        OperationCategory.Reconnect => "自动重连",
        OperationCategory.Diagnostic => "诊断",
        OperationCategory.StructRead => "结构体读取",
        OperationCategory.StructWrite => "结构体写入",
        OperationCategory.Polling => "轮询",
        _ => category.ToString()
    };

    public static string Describe(LogKind kind) => kind switch
    {
        LogKind.Operation => "操作",
        LogKind.BlockedIntent => "被拦截意图",
        LogKind.Diagnostic => "库诊断",
        _ => kind.ToString()
    };

    public static string Describe(S7ReconnectEventKind kind) => kind switch
    {
        S7ReconnectEventKind.AttemptStarted => "尝试开始",
        S7ReconnectEventKind.AttemptFailed => "尝试失败",
        S7ReconnectEventKind.Reconnected => "重连成功",
        S7ReconnectEventKind.Exhausted => "重连耗尽",
        S7ReconnectEventKind.Aborted => "重连中止",
        _ => kind.ToString()
    };
}