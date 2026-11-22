namespace S7.NetCore.Tools.Models;

public readonly record struct ByteAddressSpec(
    S7Area Area,
    int DbNumber,
    int StartByte,
    int Length);

public readonly record struct TypedAddressSpec(
    string Expression,
    S7DataType DataType,
    int Capacity);

public sealed class OperationResult<T>
{
    private OperationResult(bool isSuccess, T? value, ErrorPresentation? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public ErrorPresentation? Error { get; }

    public static OperationResult<T> Success(T value)
    {
        return new OperationResult<T>(true, value, null);
    }

    public static OperationResult<T> Failed(ErrorPresentation error)
    {
        return new OperationResult<T>(false, default, error);
    }
}

public sealed record ByteReadOutcome(
    int RequestedLength,
    int ReturnedLength,
    string HexText,
    string AsciiPreview,
    int RecoveryCount,
    bool Retried);

public sealed record ByteWriteOutcome(
    int RequestedLength,
    int BytesWritten,
    int RecoveryCount,
    bool Retried,
    bool ReplayAttempted,
    int ReplayedBytes,
    bool WriteOutcomeUncertain,
    int? WrittenRangeStart,
    int? WrittenRangeLength,
    bool VerifyPerformed,
    string? VerifyDifferenceText);

public sealed record TypedReadOutcome(
    string ValueText,
    int OccupiedBytes,
    string Address,
    int? BitNumber,
    int? StringLength,
    int? Capacity,
    int RecoveryCount,
    bool Retried);

public sealed record TypedWriteOutcome(
    int BytesWritten,
    int RecoveryCount,
    bool Retried,
    bool ReplayAttempted,
    int ReplayedBytes);