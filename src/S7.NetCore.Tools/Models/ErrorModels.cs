namespace S7.NetCore.Tools.Models;

public sealed record FieldValidationError(
    string Field,
    S7ErrorCode Code,
    string Message);

public sealed record ErrorPresentation(
    S7ErrorCategory Category,
    S7ErrorCode? Code,
    S7OperationStage? Stage,
    byte? PlcReturnCode,
    string Message,
    string SuggestedAction)
{
    public string CodeText => Code is null ? "未知" : $"{(int)Code.Value}({Code.Value})";

    public string StageText => Stage?.ToString() ?? "-";

    public string PlcReturnText => PlcReturnCode is null ? "-" : $"0x{PlcReturnCode.Value:X2}";
}