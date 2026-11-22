using S7.NetCore.Tools.Infrastructure;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Parsing;
using S7.NetCore.Tools.Services;
using S7.NetCore.Tools.Validation;

namespace S7.NetCore.Tools.ViewModels;

public sealed class TypedAccessViewModel : ObservableObject
{
    private readonly PlcSessionManager _session;
    private string _address = "DB1.DBW0";
    private S7DataType _dataType = S7DataType.Int;
    private int _capacity;
    private string _valueText = "0";
    private bool _boolValue;
    private string _addressError = string.Empty;
    private string _valueError = string.Empty;
    private string _resultText = string.Empty;
    private string _errorText = string.Empty;
    private string _recoveryText = string.Empty;
    private bool _isSuccess;
    private bool _hasAddressError;

    public TypedAccessViewModel(PlcSessionManager session)
    {
        _session = session;
        ReadCommand = new AsyncRelayCommand(ReadAsync, () => CanRead);
        WriteCommand = new AsyncRelayCommand(WriteAsync, () => CanWrite);
        _session.UiStateChanged += (_, _) => RefreshState();
        Validate();
        RefreshState();
    }

    public IReadOnlyList<S7DataType> DataTypes { get; } = Enum.GetValues<S7DataType>();

    public AsyncRelayCommand ReadCommand { get; }

    public AsyncRelayCommand WriteCommand { get; }

    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value))
            {
                Validate();
            }
        }
    }

    public S7DataType DataType
    {
        get => _dataType;
        set
        {
            if (!SetProperty(ref _dataType, value))
            {
                return;
            }

            if (IsStringType)
            {
                if (Capacity <= 0)
                {
                    Capacity = 16;
                }
            }
            else
            {
                Capacity = 0;
            }

            OnPropertyChanged(nameof(IsStringType));
            OnPropertyChanged(nameof(IsBoolType));
            Validate();
        }
    }

    public int Capacity
    {
        get => _capacity;
        set
        {
            if (SetProperty(ref _capacity, value))
            {
                Validate();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (SetProperty(ref _valueText, value))
            {
                Validate();
            }
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (SetProperty(ref _boolValue, value))
            {
                Validate();
            }
        }
    }

    public string AddressError { get => _addressError; private set => SetProperty(ref _addressError, value); }

    public string ValueError { get => _valueError; private set => SetProperty(ref _valueError, value); }

    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    public string ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }

    public string RecoveryText { get => _recoveryText; private set => SetProperty(ref _recoveryText, value); }

    public bool IsSuccess { get => _isSuccess; private set => SetProperty(ref _isSuccess, value); }

    public bool IsStringType => DataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString;

    public bool IsBoolType => DataType == S7DataType.Bool;

    public bool CanRead => !_hasAddressError && _session.State == S7SessionState.Connected && !_session.IsBusy;

    public bool CanWrite => CanRead && string.IsNullOrEmpty(ValueError);

    public void RefreshState()
    {
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanWrite));
        ReadCommand.RaiseCanExecuteChanged();
        WriteCommand.RaiseCanExecuteChanged();
    }

    private async Task ReadAsync()
    {
        ErrorText = string.Empty;
        IsSuccess = false;
        if (!ValidateAddress(out TypedAddressSpec spec))
        {
            return;
        }

        OperationResult<TypedReadOutcome> result = await _session.ReadValueAsync(spec).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorText = result.Error?.Message ?? "读取失败。";
            ResultText = string.Empty;
            return;
        }

        TypedReadOutcome outcome = result.Value;
        ValueText = outcome.ValueText;
        ResultText = $"值：{outcome.ValueText}\n占用字节：{outcome.OccupiedBytes}\n地址：{outcome.Address}"
            + (outcome.BitNumber is null ? string.Empty : $"\n位号：{outcome.BitNumber}")
            + (outcome.StringLength is null ? string.Empty : $"\n字符串长度/容量：{outcome.StringLength}/{outcome.Capacity}");
        RecoveryText = $"恢复次数 {outcome.RecoveryCount}，是否重试 {outcome.Retried}";
        IsSuccess = true;
    }

    private async Task WriteAsync()
    {
        ErrorText = string.Empty;
        IsSuccess = false;
        if (!ValidateAddress(out TypedAddressSpec spec))
        {
            return;
        }

        if (!TypedValueParser.TryParse(DataType, Capacity, ValueText, BoolValue, out S7Value value, out FieldValidationError? valueError))
        {
            ValueError = valueError?.Message ?? "值非法。";
            return;
        }

        OperationResult<TypedWriteOutcome> result = await _session.WriteValueAsync(spec, value).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorText = result.Error?.Message ?? "写入失败。";
            return;
        }

        TypedWriteOutcome outcome = result.Value;
        ResultText = $"写入成功：{outcome.BytesWritten} 字节。";
        RecoveryText = $"恢复次数 {outcome.RecoveryCount}，是否重试 {outcome.Retried}，是否重放 {outcome.ReplayAttempted}";
        IsSuccess = true;
    }

    private bool ValidateAddress(out TypedAddressSpec spec)
    {
        spec = new TypedAddressSpec(Address, DataType, Capacity);
        TypedValidationResult validation = TypedInputValidator.Validate(spec);
        _hasAddressError = !validation.IsValid;
        AddressError = validation.IsValid ? string.Empty : validation.Error?.Message ?? "地址非法。";
        ValidateValue();
        RefreshState();
        return validation.IsValid;
    }

    private void ValidateValue()
    {
        if (TypedValueParser.TryParse(DataType, Capacity, ValueText, BoolValue, out _, out FieldValidationError? error))
        {
            ValueError = string.Empty;
        }
        else
        {
            ValueError = error?.Message ?? "值非法。";
        }
    }

    private void Validate()
    {
        ValidateAddress(out _);
    }
}