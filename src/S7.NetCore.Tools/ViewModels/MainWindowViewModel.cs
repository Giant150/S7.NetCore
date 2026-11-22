using System.Collections.ObjectModel;
using System.Reflection;
using S7.NetCore.Tools.Infrastructure;
using S7.NetCore.Tools.Models;
using S7.NetCore.Tools.Services;

namespace S7.NetCore.Tools.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly PlcSessionManager _session;
    private readonly DialogService _dialog;
    private string _stateText = string.Empty;
    private string _pduText = "PDU -";
    private string _operationBlockedReason = string.Empty;
    private string _reconnectProgressText = string.Empty;
    private string _busyText = string.Empty;
    private bool _isBusy;
    private bool _isWriteUnlocked;
    private bool _canDisconnect;
    private bool _canToggleWriteProtection = true;

    public MainWindowViewModel(
        PlcSessionManager session,
        DialogService dialog,
        ConnectionViewModel connection,
        ByteAccessViewModel byteAccess,
        TypedAccessViewModel typedAccess,
        LogViewModel log)
    {
        _session = session;
        _dialog = dialog;
        Connection = connection;
        ByteAccess = byteAccess;
        TypedAccess = typedAccess;
        Log = log;
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect);
        ToggleWriteProtectionCommand = new RelayCommand(ToggleWriteProtection, () => CanToggleWriteProtection);
        _session.UiStateChanged += (_, _) => Refresh();
        Refresh();
    }

    public string Title => Texts.WindowTitle;

    public ConnectionViewModel Connection { get; }

    public ByteAccessViewModel ByteAccess { get; }

    public TypedAccessViewModel TypedAccess { get; }

    public LogViewModel Log { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public RelayCommand ToggleWriteProtectionCommand { get; }

    public ObservableCollection<SessionTimelineEntry> Timeline => _session.Timeline;

    public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }

    public string PduText { get => _pduText; private set => SetProperty(ref _pduText, value); }

    public string OperationBlockedReason
    {
        get => _operationBlockedReason;
        private set => SetProperty(ref _operationBlockedReason, value);
    }

    public string ReconnectProgressText
    {
        get => _reconnectProgressText;
        private set => SetProperty(ref _reconnectProgressText, value);
    }

    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public bool IsWriteUnlocked
    {
        get => _isWriteUnlocked;
        private set
        {
            if (SetProperty(ref _isWriteUnlocked, value))
            {
                OnPropertyChanged(nameof(WriteProtectionText));
            }
        }
    }

    public string WriteProtectionText => IsWriteUnlocked ? "写保护：已解锁" : "写保护：已锁定";

    public bool CanDisconnect { get => _canDisconnect; private set => SetProperty(ref _canDisconnect, value); }

    public bool CanToggleWriteProtection
    {
        get => _canToggleWriteProtection;
        private set => SetProperty(ref _canToggleWriteProtection, value);
    }

    public string VersionText
    {
        get
        {
            string toolVersion = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString()
                ?? "未知";
            string libraryVersion = typeof(S7Client).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(S7Client).Assembly.GetName().Version?.ToString()
                ?? "未知";
            return $"工具 {toolVersion} · S7.NetCore {libraryVersion}";
        }
    }

    public Task ShutdownAsync()
    {
        return _session.ShutdownAsync();
    }

    private void Refresh()
    {
        ConnectionUiState policy = ConnectionStatePolicy.Compute(
            _session.State,
            _session.IsBusy,
            _session.IsWriteUnlocked);

        StateText = Texts.Describe(_session.State);
        PduText = _session.NegotiatedPduSize > 0 ? $"PDU {_session.NegotiatedPduSize}" : "PDU -";
        OperationBlockedReason = policy.OperationBlockedReason ?? string.Empty;
        ReconnectProgressText = _session.ReconnectProgressText;
        BusyText = _session.BusyText;
        IsBusy = _session.IsBusy;
        IsWriteUnlocked = _session.IsWriteUnlocked;
        CanDisconnect = policy.CanDisconnect;
        CanToggleWriteProtection = policy.CanToggleWriteProtection;

        Connection.RefreshState();
        ByteAccess.RefreshState();
        TypedAccess.RefreshState();
        DisconnectCommand.RaiseCanExecuteChanged();
        ToggleWriteProtectionCommand.RaiseCanExecuteChanged();
    }

    private async Task DisconnectAsync()
    {
        OperationResult<bool> result = await _session.DisconnectAsync().ConfigureAwait(true);
        if (!result.IsSuccess && result.Error is not null)
        {
            await _dialog.ShowErrorAsync(result.Error).ConfigureAwait(true);
        }
    }

    private void ToggleWriteProtection()
    {
        _session.SetWriteUnlocked(!_session.IsWriteUnlocked);
        Refresh();
    }
}