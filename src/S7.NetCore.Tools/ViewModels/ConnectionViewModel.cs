using S7.NetCore.Tools.Infrastructure;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Services;
using S7.NetCore.Tools.Validation;

namespace S7.NetCore.Tools.ViewModels;

public sealed class ConnectionViewModel : ObservableObject
{
    private readonly PlcSessionManager _session;
    private readonly DialogService _dialog;
    private string _validationSummary = string.Empty;
    private bool _hasErrors;
    private bool _isEditable = true;
    private bool _canConnect = true;

    public ConnectionViewModel(PlcSessionManager session, DialogService dialog)
    {
        _session = session;
        _dialog = dialog;
        Settings = new ConnectionSettingsModel();
        Settings.PropertyChanged += (_, _) => OnSettingsChanged();
        Settings.Reconnect.PropertyChanged += (_, _) => OnSettingsChanged();
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect, ShowException);
        _session.UiStateChanged += (_, _) => RefreshState();
        Validate();
        RefreshState();
    }

    public ConnectionSettingsModel Settings { get; }

    public ReconnectSettingsModel Reconnect => Settings.Reconnect;

    public IReadOnlyList<S7CpuType> CpuTypes { get; } = Enum.GetValues<S7CpuType>();

    public AsyncRelayCommand ConnectCommand { get; }

    public string MaxAttemptsHint => "0 表示不限次数";

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public bool HasErrors
    {
        get => _hasErrors;
        private set => SetProperty(ref _hasErrors, value);
    }

    public bool IsEditable
    {
        get => _isEditable;
        private set => SetProperty(ref _isEditable, value);
    }

    public bool CanConnect
    {
        get => _canConnect;
        private set => SetProperty(ref _canConnect, value);
    }

    public void RefreshState()
    {
        ConnectionUiState policy = ConnectionStatePolicy.Compute(
            _session.State,
            _session.IsBusy,
            _session.IsWriteUnlocked);
        IsEditable = policy.CanEditSettings;
        CanConnect = policy.CanConnect && !HasErrors;
        ConnectCommand.RaiseCanExecuteChanged();
    }

    private async Task ConnectAsync()
    {
        OperationResult<int> result = await _session.ConnectAsync(Settings).ConfigureAwait(true);
        if (!result.IsSuccess && result.Error is not null)
        {
            ValidationSummary = result.Error.Message;
            await _dialog.ShowErrorAsync(result.Error).ConfigureAwait(true);
        }
        else
        {
            ValidationSummary = string.Empty;
        }

        RefreshState();
    }

    private void OnSettingsChanged()
    {
        Validate();
        RefreshState();
    }

    private void Validate()
    {
        IReadOnlyList<FieldValidationError> errors = ConnectionSettingsValidator.Validate(Settings);
        HasErrors = errors.Count > 0;
        ValidationSummary = errors.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, errors.Select(static error => $"• {error.Message}"));
        RefreshState();
    }

    private void ShowException(Exception exception)
    {
        ValidationSummary = exception.Message;
    }
}