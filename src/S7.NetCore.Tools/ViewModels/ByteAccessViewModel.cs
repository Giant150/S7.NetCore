using S7.NetCore.Tools.Infrastructure;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Parsing;
using S7.NetCore.Tools.Services;

namespace S7.NetCore.Tools.ViewModels;

public sealed class ByteAccessViewModel : ObservableObject
{
    private readonly PlcSessionManager _session;
    private S7Area _area = S7Area.DataBlock;
    private int _dbNumber = 1;
    private int _startByte;
    private int _length = 16;
    private string _hexInput = "00 01 02 03 04 05 06 07";
    private int _groupSize = 16;
    private bool _verifyAfterWrite;
    private string _resultHex = string.Empty;
    private string _resultAscii = string.Empty;
    private string _statusText = "尚未执行操作。";
    private string _errorText = string.Empty;
    private string _recoveryText = string.Empty;
    private string _requestedLengthText = string.Empty;
    private string _returnedLengthText = string.Empty;

    public ByteAccessViewModel(PlcSessionManager session)
    {
        _session = session;
        ReadCommand = new AsyncRelayCommand(ReadAsync, () => CanRead);
        WriteCommand = new AsyncRelayCommand(WriteAsync, () => CanWrite);
        _session.UiStateChanged += (_, _) => UpdateCommands();
        UpdateCommands();
    }

    public IReadOnlyList<S7Area> Areas { get; } = Enum.GetValues<S7Area>();

    public IReadOnlyList<int> GroupSizes { get; } = [1, 2, 4, 8, 16];

    public AsyncRelayCommand ReadCommand { get; }

    public AsyncRelayCommand WriteCommand { get; }

    public S7Area Area
    {
        get => _area;
        set
        {
            if (SetProperty(ref _area, value))
            {
                OnPropertyChanged(nameof(IsDbEnabled));
                OnPropertyChanged(nameof(IsInputArea));
                UpdateCommands();
            }
        }
    }

    public int DbNumber { get => _dbNumber; set => SetProperty(ref _dbNumber, value); }

    public int StartByte { get => _startByte; set => SetProperty(ref _startByte, value); }

    public int Length { get => _length; set => SetProperty(ref _length, value); }

    public string HexInput { get => _hexInput; set => SetProperty(ref _hexInput, value); }

    public int GroupSize { get => _groupSize; set => SetProperty(ref _groupSize, value); }

    public bool VerifyAfterWrite { get => _verifyAfterWrite; set => SetProperty(ref _verifyAfterWrite, value); }

    public string ResultHex { get => _resultHex; private set => SetProperty(ref _resultHex, value); }

    public string ResultAscii { get => _resultAscii; private set => SetProperty(ref _resultAscii, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    public string RecoveryText { get => _recoveryText; private set => SetProperty(ref _recoveryText, value); }

    public string RequestedLengthText { get => _requestedLengthText; private set => SetProperty(ref _requestedLengthText, value); }

    public string ReturnedLengthText { get => _returnedLengthText; private set => SetProperty(ref _returnedLengthText, value); }

    public bool IsDbEnabled => Area == S7Area.DataBlock;

    public bool IsInputArea => Area == S7Area.Input;

    public bool CanRead => _session.State == S7SessionState.Connected && !_session.IsBusy;

    public bool CanWrite => CanRead && Area != S7Area.Input;

    public void RefreshState() => UpdateCommands();

    private async Task ReadAsync()
    {
        ErrorText = string.Empty;
        if (!TryCreateSpec(out ByteAddressSpec spec, out string? validationError))
        {
            ErrorText = validationError!;
            return;
        }

        OperationResult<ByteReadOutcome> result = await _session.ReadBytesAsync(spec).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorText = result.Error?.Message ?? "读取失败。";
            StatusText = "读取失败。";
            return;
        }

        ByteReadOutcome outcome = result.Value;
        ResultHex = HexByteCodec.Format(
            Convert.FromHexString(outcome.HexText.Replace(" ", string.Empty, StringComparison.Ordinal)),
            GroupSize).Hex;
        ResultAscii = outcome.AsciiPreview;
        RequestedLengthText = outcome.RequestedLength.ToString();
        ReturnedLengthText = outcome.ReturnedLength.ToString();
        RecoveryText = $"恢复次数 {outcome.RecoveryCount}，是否重试 {outcome.Retried}";
        StatusText = $"读取成功：{outcome.ReturnedLength} 字节。";
    }

    private async Task WriteAsync()
    {
        ErrorText = string.Empty;
        if (!TryCreateSpec(out ByteAddressSpec spec, out string? validationError))
        {
            ErrorText = validationError!;
            return;
        }

        if (!HexByteCodec.TryParse(HexInput, out byte[] bytes, out string? hexError))
        {
            ErrorText = hexError!;
            return;
        }

        if (bytes.Length != spec.Length)
        {
            ErrorText = $"实际输入 {bytes.Length} 字节与声明长度 {spec.Length} 不一致。";
            return;
        }

        OperationResult<ByteWriteOutcome> result = await _session.WriteBytesAsync(
            spec,
            bytes,
            VerifyAfterWrite).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorText = result.Error?.Message ?? "写入失败。";
            StatusText = "写入失败。";
            return;
        }

        ByteWriteOutcome outcome = result.Value;
        StatusText = $"写入成功：{outcome.BytesWritten} 字节。"
            + (outcome.VerifyPerformed
                ? outcome.VerifyDifferenceText is null ? " 回读一致。" : $" 回读差异：{outcome.VerifyDifferenceText}"
                : string.Empty);
        RecoveryText = $"恢复次数 {outcome.RecoveryCount}，是否重试 {outcome.Retried}，是否重放 {outcome.ReplayAttempted}";
    }

    private bool TryCreateSpec(out ByteAddressSpec spec, out string? error)
    {
        spec = default;
        if (Area == S7Area.DataBlock && DbNumber is < 1 or > 65535)
        {
            error = "DB 号必须在 1 至 65535 之间。";
            return false;
        }

        if (StartByte is < 0 or > 65535)
        {
            error = "起始字节必须在 0 至 65535 之间。";
            return false;
        }

        if (Length is < 1 or > 65535)
        {
            error = "长度必须在 1 至 65535 之间。";
            return false;
        }

        spec = new ByteAddressSpec(Area, Area == S7Area.DataBlock ? DbNumber : 0, StartByte, Length);
        error = null;
        return true;
    }

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanWrite));
        ReadCommand.RaiseCanExecuteChanged();
        WriteCommand.RaiseCanExecuteChanged();
    }
}