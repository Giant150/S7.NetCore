namespace S7.NetCore.Tests.VirtualPlc.Server;

/// <summary>
/// 进程内嵌入式 S7Server 集合夹具。初始化只写快照，不抛跳过异常。
/// </summary>
public sealed class VirtualPlcServerFixture : IAsyncLifetime
{
    private VirtualPlcServerHost? _host;
    private bool _initialized;
    private VirtualPlcServerSnapshot _snapshot = VirtualPlcServerSnapshot.Initial;
    private CancellationTokenSource? _startupScope;

    private static readonly SemaphoreSlim StartLock = new(1, 1);

    internal static VirtualPlcServerFixture? Current { get; private set; }

    internal static async Task<VirtualPlcServerFixture> EnsureStartedAsync()
    {
        if (Current is { _initialized: true } current)
        {
            return current;
        }

        await StartLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Current is { _initialized: true } existing)
            {
                return existing;
            }

            var fixture = new VirtualPlcServerFixture();
            await fixture.InitializeAsync().ConfigureAwait(false);
            return fixture;
        }
        finally
        {
            StartLock.Release();
        }
    }

    internal VirtualPlcServerSnapshot Snapshot => _host?.Snapshot ?? _snapshot;

    internal VirtualPlcEndpoint Endpoint => Snapshot.Endpoint;

    internal IReadOnlyList<VirtualPlcRegisteredArea> RegisteredAreas => _host?.RegisteredAreas ?? [];

    internal int ClientsCount => _host?.ClientsCount ?? -1;

    internal ReadOnlyMemory<byte> CopyServerRange(
        S7.NetCore.S7Area area,
        int dbNumber,
        int startByte,
        int length)
    {
        return _host?.CopyServerRange(area, dbNumber, startByte, length)
            ?? throw new InvalidOperationException("服务器已停止，无法访问区域缓冲区。");
    }

    public async Task InitializeAsync()
    {
        _startupScope = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            VirtualPlcServerSettings settings = VirtualPlcServerSettings.Parse();

            if (settings.Mode == VirtualPlcServerMode.Off)
            {
                _snapshot = VirtualPlcServerSnapshot.EnvironmentSkip(
                    VirtualPlcSkipReason.ModeOff,
                    "S7_VIRTUAL_PLC_SERVER=off");
                return;
            }

            if (settings.Mode == VirtualPlcServerMode.External)
            {
                VirtualPlcProbe probe = await VirtualPlcEnvironment
                    .ProbeAsync(settings.Endpoint, _startupScope.Token)
                    .ConfigureAwait(false);

                _snapshot = probe.Ready
                    ? new VirtualPlcServerSnapshot
                    {
                        Kind = VirtualPlcServerSnapshotKind.Ready,
                        IsRunning = false,
                        Endpoint = settings.Endpoint,
                        Detail = "使用外部 S7 服务端。"
                    }
                    : VirtualPlcServerSnapshot.EnvironmentSkip(
                        VirtualPlcSkipReason.ExternalEndpointUnreachable,
                        probe.Reason);

                return;
            }

            VirtualPlcPresenceProbe library = VirtualPlcLibraryPresence.Probe();
            if (!library.Available)
            {
                _snapshot = VirtualPlcServerSnapshot.EnvironmentSkip(library.Reason, library.Detail);
                return;
            }

            _host = new VirtualPlcServerHost(settings);
            _snapshot = await _host.StartAsync(_startupScope.Token).ConfigureAwait(false);

            if (_snapshot.Kind == VirtualPlcServerSnapshotKind.Ready)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
        }
        catch (VirtualPlcEnvironmentSkipException exception)
        {
            _snapshot = VirtualPlcServerSnapshot.EnvironmentSkip(exception.Reason, exception.Detail);
        }
        catch (FormatException exception)
        {
            _snapshot = VirtualPlcServerSnapshot.EnvironmentSkip(
                VirtualPlcSkipReason.ConfigurationInvalid,
                exception.Message);
        }
        catch (VirtualPlcFailureException exception)
        {
            _snapshot = VirtualPlcServerSnapshot.Failure(exception.Stage, exception.Message);
        }
        catch (Exception exception)
        {
            _snapshot = VirtualPlcServerSnapshot.Failure("夹具启动", exception.ToString());
        }
        finally
        {
            if (_snapshot.Kind != VirtualPlcServerSnapshotKind.Ready)
            {
                _host = null;
            }

            Current = this;
            _initialized = true;
        }
    }

    public async Task DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        if (_host is not null)
        {
            try
            {
                await _host.StopAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _startupScope?.Dispose();
        Current = null;
    }

    internal Task StopForLifecycleTestAsync()
    {
        return _host?.StopForLifecycleTestAsync(CancellationToken.None) ?? Task.CompletedTask;
    }

    internal Task<VirtualPlcServerSnapshot> RestartForLifecycleTestAsync()
    {
        return _host?.RestartForLifecycleTestAsync(CancellationToken.None)
            ?? Task.FromResult(Snapshot);
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        try
        {
            _host?.StopAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }
}