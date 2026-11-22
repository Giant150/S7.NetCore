using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Services;

public sealed class ErrorPresentationMapper
{
    public ErrorPresentation Map(S7ClientException exception, TimeSpan elapsed)
    {
        string message = exception.ErrorCode switch
        {
            S7ErrorCode.InvalidAddress => "IP 地址非法。",
            S7ErrorCode.InvalidPort => "端口非法。",
            S7ErrorCode.InvalidCpuType => "CPU 型号非法。",
            S7ErrorCode.InvalidRackSlot => "机架号或槽位号非法。",
            S7ErrorCode.InvalidTimeout => "超时设置非法。",
            S7ErrorCode.InvalidPduSize => "PDU 大小非法。",
            S7ErrorCode.InvalidReconnectPolicy => "自动重连策略非法。",
            S7ErrorCode.SessionNotEstablished => "会话未建立，请先连接 PLC。",
            S7ErrorCode.SessionAlreadyConnected => "会话已连接，请先断开当前会话。",
            S7ErrorCode.SessionFaulted => "会话处于异常状态，请断开后重新连接。",
            S7ErrorCode.SessionReconnecting => "正在自动重连，暂不可读写（错误码 204）。",
            S7ErrorCode.ConnectionReset => "连接已中断。",
            S7ErrorCode.TcpConnectFailed => "TCP 连接失败，请检查 IP、端口与网络。",
            S7ErrorCode.ConnectTimeout => "连接超时，请检查 PLC 是否在线。",
            S7ErrorCode.ReadTimeout => "读取超时，请检查链路或稍后重试。",
            S7ErrorCode.WriteTimeout => "写入超时，PLC 可能已执行部分写入。",
            S7ErrorCode.OperationCancelled => "操作已取消。",
            S7ErrorCode.ReconnectExhausted => "自动重连尝试次数已耗尽，会话回到未连接。",
            S7ErrorCode.ReconnectAborted => "自动重连已被中止。",
            S7ErrorCode.AddressSyntaxInvalid => "地址表达式语法非法。",
            S7ErrorCode.BitNumberOutOfRange => "位号必须在 0 至 7 之间。",
            S7ErrorCode.DataTypeNotSupported => "数据类型不受支持。",
            S7ErrorCode.DataTypeAddressMismatch => "数据类型与地址访问宽度不兼容。",
            S7ErrorCode.ValueOutOfRange => "写入值超出目标类型范围。",
            S7ErrorCode.StringEncodingInvalid => "字符串包含非 ASCII 字符。",
            S7ErrorCode.StringCapacityInvalid => "字符串容量非法或内容超长。",
            S7ErrorCode.AddressOutOfRange => "地址或长度超出区域容量。",
            S7ErrorCode.ValueTypeMismatch => "写入值类型与目标类型不匹配。",
            S7ErrorCode.AreaNotSupported => "PLC 输入区只读，禁止写入。",
            S7ErrorCode.PlcAddressOutOfRange => "PLC 报告地址越界（返回码 0x05）。",
            S7ErrorCode.PlcObjectNotExist => "PLC 报告对象不存在（返回码 0x0A）。",
            _ => exception.Message
        };

        string action = exception.ErrorCode switch
        {
            S7ErrorCode.SessionNotEstablished or S7ErrorCode.SessionFaulted => "请检查连接参数并重新连接。",
            S7ErrorCode.SessionAlreadyConnected => "请先断开当前会话。",
            S7ErrorCode.SessionReconnecting => "等待重连完成，或断开后手动重新连接。",
            S7ErrorCode.ReadTimeout or S7ErrorCode.WriteTimeout => "检查网络与 PLC 状态；写入超时请确认 PLC 实际数据。",
            S7ErrorCode.ValueOutOfRange or S7ErrorCode.StringCapacityInvalid or S7ErrorCode.StringEncodingInvalid
                => "请修正输入值后重试。",
            S7ErrorCode.AreaNotSupported => "请选择输出区、位存储区或数据块进行写入。",
            S7ErrorCode.ReconnectExhausted => "重新连接 PLC，或调整自动重连策略。",
            _ => "查看操作日志以获取更多诊断信息。"
        };

        return new ErrorPresentation(
            exception.Category,
            exception.ErrorCode,
            exception.Stage,
            exception.PlcReturnCode,
            message,
            action);
    }

    public ErrorPresentation MapValidation(FieldValidationError error)
    {
        return new ErrorPresentation(
            S7ErrorCategory.InvalidArgument,
            error.Code,
            S7OperationStage.Validate,
            null,
            error.Message,
            "请修正输入后重试。");
    }

    public ErrorPresentation MapUnknown(Exception exception)
    {
        return new ErrorPresentation(
            S7ErrorCategory.Protocol,
            null,
            null,
            null,
            $"未知错误：{exception.Message}",
            "查看操作日志或重新启动工具。");
    }
}
