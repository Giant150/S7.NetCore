namespace S7.NetCore.Tools.Services;

public readonly record struct ConnectionUiState(
    bool CanEditSettings,
    bool CanConnect,
    bool CanDisconnect,
    bool CanExecuteOperations,
    bool CanToggleWriteProtection,
    string? OperationBlockedReason);

public static class ConnectionStatePolicy
{
    public static ConnectionUiState Compute(S7SessionState state, bool isBusy, bool writeUnlocked)
    {
        bool connected = state == S7SessionState.Connected;
        bool configurable = state is S7SessionState.Disconnected or S7SessionState.Faulted or S7SessionState.Reconnecting;
        string? blockedReason = state switch
        {
            S7SessionState.Disconnected => "会话未建立。",
            S7SessionState.Connecting => "正在连接，暂不可读写。",
            S7SessionState.Reconnecting => "正在重连，暂不可读写（错误码 204）。",
            S7SessionState.Faulted => "会话异常，请断开后重新连接。",
            _ => null
        };

        return new ConnectionUiState(
            CanEditSettings: configurable && !isBusy,
            CanConnect: state is S7SessionState.Disconnected or S7SessionState.Faulted && !isBusy,
            CanDisconnect: state is not S7SessionState.Disconnected,
            CanExecuteOperations: connected && !isBusy,
            CanToggleWriteProtection: connected || state == S7SessionState.Disconnected,
            OperationBlockedReason: blockedReason);
    }
}