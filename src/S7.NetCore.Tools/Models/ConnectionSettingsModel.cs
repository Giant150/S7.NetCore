using S7.NetCore.Tools.Infrastructure;

namespace S7.NetCore.Tools.Models;

public sealed class ConnectionSettingsModel : ObservableObject
{
    private string _ipAddress = "192.168.0.1";
    private S7CpuType _cpuType = S7CpuType.S71200;
    private int _port = 102;
    private int _rack;
    private int _slot;
    private byte? _localTsapFirst;
    private byte? _localTsapSecond;
    private byte? _remoteTsapFirst;
    private byte? _remoteTsapSecond;
    private int _connectTimeoutSeconds = 5;
    private int _readTimeoutSeconds = 10;
    private int _writeTimeoutSeconds = 10;
    private int _requestedPduSize = 480;
    private int _maxQueuedRequests = 32;
    private bool _enableProtocolTrace;

    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }

    public S7CpuType CpuType { get => _cpuType; set => SetProperty(ref _cpuType, value); }

    public int Port { get => _port; set => SetProperty(ref _port, value); }

    public int Rack { get => _rack; set => SetProperty(ref _rack, value); }

    public int Slot { get => _slot; set => SetProperty(ref _slot, value); }

    public byte? LocalTsapFirst { get => _localTsapFirst; set => SetProperty(ref _localTsapFirst, value); }

    public byte? LocalTsapSecond { get => _localTsapSecond; set => SetProperty(ref _localTsapSecond, value); }

    public byte? RemoteTsapFirst { get => _remoteTsapFirst; set => SetProperty(ref _remoteTsapFirst, value); }

    public byte? RemoteTsapSecond { get => _remoteTsapSecond; set => SetProperty(ref _remoteTsapSecond, value); }

    public int ConnectTimeoutSeconds { get => _connectTimeoutSeconds; set => SetProperty(ref _connectTimeoutSeconds, value); }

    public int ReadTimeoutSeconds { get => _readTimeoutSeconds; set => SetProperty(ref _readTimeoutSeconds, value); }

    public int WriteTimeoutSeconds { get => _writeTimeoutSeconds; set => SetProperty(ref _writeTimeoutSeconds, value); }

    public int RequestedPduSize { get => _requestedPduSize; set => SetProperty(ref _requestedPduSize, value); }

    public int MaxQueuedRequests { get => _maxQueuedRequests; set => SetProperty(ref _maxQueuedRequests, value); }

    public bool EnableProtocolTrace { get => _enableProtocolTrace; set => SetProperty(ref _enableProtocolTrace, value); }

    public ReconnectSettingsModel Reconnect { get; } = new();
}

public sealed class ReconnectSettingsModel : ObservableObject
{
    private bool _autoReconnectEnabled;
    private int _maxAttempts = 5;
    private int _initialDelayMilliseconds = 1000;
    private int _maxDelaySeconds = 30;
    private double _backoffMultiplier = 2.0;
    private double _jitterFactor = 0.2;
    private int _waitTimeoutSeconds = 10;
    private bool _reconnectOnTimeout;
    private bool _replayUncertainWrites;

    public bool AutoReconnectEnabled
    {
        get => _autoReconnectEnabled;
        set => SetProperty(ref _autoReconnectEnabled, value);
    }

    public int MaxAttempts { get => _maxAttempts; set => SetProperty(ref _maxAttempts, value); }

    public int InitialDelayMilliseconds
    {
        get => _initialDelayMilliseconds;
        set => SetProperty(ref _initialDelayMilliseconds, value);
    }

    public int MaxDelaySeconds { get => _maxDelaySeconds; set => SetProperty(ref _maxDelaySeconds, value); }

    public double BackoffMultiplier
    {
        get => _backoffMultiplier;
        set => SetProperty(ref _backoffMultiplier, value);
    }

    public double JitterFactor { get => _jitterFactor; set => SetProperty(ref _jitterFactor, value); }

    public int WaitTimeoutSeconds { get => _waitTimeoutSeconds; set => SetProperty(ref _waitTimeoutSeconds, value); }

    public bool ReconnectOnTimeout
    {
        get => _reconnectOnTimeout;
        set => SetProperty(ref _reconnectOnTimeout, value);
    }

    public bool ReplayUncertainWrites
    {
        get => _replayUncertainWrites;
        set => SetProperty(ref _replayUncertainWrites, value);
    }
}