using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Snap7;
using S7.NetCore;
using S7.NetCore.Tests.Native;
using S7Client = S7.NetCore.S7Client;

namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed class VirtualPlcServerHost
{
    private static readonly object ActiveLock = new();
    private static VirtualPlcServerHost? _active;

    private readonly VirtualPlcServerSettings _settings;
    private S7Server? _server;
    private VirtualPlcRegionRegistry? _regions;

    internal VirtualPlcServerHost(VirtualPlcServerSettings settings)
    {
        _settings = settings;
    }

    internal VirtualPlcServerSnapshot Snapshot { get; private set; } = VirtualPlcServerSnapshot.Initial;

    internal VirtualPlcEndpoint Endpoint => _settings.Endpoint;

    internal IReadOnlyList<VirtualPlcRegisteredArea> RegisteredAreas => _regions?.Areas ?? [];

    internal int ClientsCount => _server?.ClientsCount ?? -1;

    internal ReadOnlyMemory<byte> CopyServerRange(
        S7Area area,
        int dbNumber,
        int startByte,
        int length)
    {
        VirtualPlcRegionRegistry regions = _regions
            ?? throw new InvalidOperationException("服务器已停止，无法访问区域缓冲区。");

        return regions.GetForArea(area, dbNumber).CopyRange(startByte, length);
    }

    internal async Task<VirtualPlcServerSnapshot> StartAsync(CancellationToken cancellationToken)
    {
        lock (ActiveLock)
        {
            if (_active is not null && !ReferenceEquals(_active, this))
            {
                throw new VirtualPlcFailureException(
                    "单实例守卫",
                    "已有活动的嵌入式服务器实例（AlreadyRunning）。");
            }

            if (_server is not null)
            {
                throw new VirtualPlcFailureException("启动", "服务器已运行（AlreadyRunning）。");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        VirtualPlcNativeLibraryProbe probe = VirtualPlcNativeLibrary.Probe(_settings.LibraryPath);
        if (!probe.Available)
        {
            throw new VirtualPlcEnvironmentSkipException(probe.Reason, probe.Detail);
        }

        VirtualPlcNativeLibrary.EnsureResolver(_settings.LibraryPath);

        S7Server? server = null;
        VirtualPlcRegionRegistry? regions = null;

        try
        {
            server = new S7Server();
            if (NativeServerInterop.GetHandle(server) == IntPtr.Zero)
            {
                throw new VirtualPlcFailureException("Srv_Create", "Srv_Create 返回空句柄。");
            }

            SetParameters(server);

            regions = new VirtualPlcRegionRegistry();
            regions.Register(server);

            int startResult = server.StartTo(_settings.Host);
            if (startResult != 0)
            {
                string errorText = server.ErrorText(startResult);

                if (IsPortInUse(errorText))
                {
                    throw new VirtualPlcEnvironmentSkipException(
                        VirtualPlcSkipReason.PortInUse,
                        $"端口 {_settings.Port} 被占用：{errorText}");
                }

                throw new VirtualPlcFailureException("绑定启动", errorText, startResult);
            }

            VirtualPlcObservedBaseline baseline = await SelfTestAsync(cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new VirtualPlcFailureException(
                    "启动耗时",
                    $"启动耗时 {stopwatch.Elapsed.TotalSeconds:F2}s 超过 5s。");
            }

            baseline = baseline with { StartupMilliseconds = stopwatch.Elapsed.TotalMilliseconds };

            _server = server;
            _regions = regions;
            lock (ActiveLock)
            {
                _active = this;
            }

            Snapshot = new VirtualPlcServerSnapshot
            {
                Kind = VirtualPlcServerSnapshotKind.Ready,
                IsRunning = true,
                Endpoint = _settings.Endpoint,
                NegotiatedPduSize = baseline.NegotiatedPduSize,
                Baseline = baseline,
                RegisteredAreas = regions.Areas,
                StartupMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };

            return Snapshot;
        }
        catch (VirtualPlcEnvironmentSkipException)
        {
            Cleanup(server, regions);
            throw;
        }
        catch (Exception exception)
        {
            Cleanup(server, regions);
            if (exception is VirtualPlcFailureException)
            {
                throw;
            }

            throw new VirtualPlcFailureException("启动", exception.Message, innerException: exception);
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        S7Server? server = _server;
        VirtualPlcRegionRegistry? regions = _regions;
        _server = null;
        _regions = null;

        lock (ActiveLock)
        {
            if (ReferenceEquals(_active, this))
            {
                _active = null;
            }
        }

        if (server is null)
        {
            Snapshot = new VirtualPlcServerSnapshot
            {
                Kind = VirtualPlcServerSnapshotKind.Ready,
                IsRunning = false,
                Endpoint = _settings.Endpoint,
                Detail = "服务器已停止。"
            };

            return;
        }

        int stopResult = server.Stop();
        if (stopResult != 0)
        {
            string errorText = server.ErrorText(stopResult);
            Cleanup(server, regions);
            throw new VirtualPlcFailureException("停止", errorText, stopResult);
        }

        if (regions is not null)
        {
            regions.UnregisterAll(server);
        }

        NativeServerInterop.Destroy(server);
        VerifyPortReleased();

        Snapshot = new VirtualPlcServerSnapshot
        {
            Kind = VirtualPlcServerSnapshotKind.Ready,
            IsRunning = false,
            Endpoint = _settings.Endpoint,
            Detail = "服务器已停止，端口已释放。"
        };

        await Task.CompletedTask.ConfigureAwait(false);
    }

    internal async Task<VirtualPlcServerSnapshot> RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task StopForLifecycleTestAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<VirtualPlcServerSnapshot> RestartForLifecycleTestAsync(
        CancellationToken cancellationToken)
    {
        return await RestartAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetParameters(S7Server server)
    {
        ushort port = (ushort)_settings.Port;
        int result = server.SetParam(S7Consts.p_u16_LocalPort, ref port);
        if (result != 0)
        {
            throw new VirtualPlcFailureException(
                "设置本地端口",
                server.ErrorText(result),
                result);
        }

        int maxClients = _settings.MaxClients;
        result = server.SetParam(S7Consts.p_i32_MaxClients, ref maxClients);
        if (result != 0)
        {
            throw new VirtualPlcFailureException(
                "设置并发上限",
                server.ErrorText(result),
                result);
        }

        server.CpuStatus = 8;
    }

    private async Task<VirtualPlcObservedBaseline> SelfTestAsync(CancellationToken cancellationToken)
    {
        S7ConnectionOptions options = _settings.Endpoint.ToOptions() with
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ReadTimeout = TimeSpan.FromSeconds(3),
            WriteTimeout = TimeSpan.FromSeconds(3)
        };

        await using var client = new S7Client(options);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        ReadOnlyMemory<byte> initial = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 1),
            cancellationToken).ConfigureAwait(false);

        if (initial.Length != 1 || initial.Span[0] != 0x0B)
        {
            throw new VirtualPlcFailureException(
                "自检初值",
                $"DB1.DBB0 期望 0x0B，实际 {(initial.Length == 1 ? $"0x{initial.Span[0]:X2}" : $"长度 {initial.Length}")}。");
        }

        int pduSize = client.NegotiatedPduSize;
        if (pduSize <= 0)
        {
            throw new VirtualPlcFailureException("自检 PDU", $"协商 PDU 非法：{pduSize}。");
        }

        byte outOfRangeCode = await ProbePlcErrorAsync(
            client,
            new S7ReadRequest(S7Area.DataBlock, 1, 65535, 2),
            "越界探针",
            expectedCode: 0x05,
            cancellationToken).ConfigureAwait(false);

        byte objectNotExistCode = await ProbePlcErrorAsync(
            client,
            new S7ReadRequest(S7Area.DataBlock, 4, 0, 2),
            "对象不存在探针",
            expectedCode: 0x0A,
            cancellationToken).ConfigureAwait(false);

        await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        return new VirtualPlcObservedBaseline(
            pduSize,
            pduSize - 18,
            pduSize - 35,
            outOfRangeCode,
            objectNotExistCode,
            0);
    }

    private static async Task<byte> ProbePlcErrorAsync(
        S7Client client,
        S7ReadRequest request,
        string stage,
        byte expectedCode,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await client.ReadBytesAsync(request, cancellationToken).ConfigureAwait(false);
            throw new VirtualPlcFailureException(stage, "期望 PLC 返回错误，但读取成功。");
        }
        catch (S7ClientException exception)
        {
            if (exception.Category != S7ErrorCategory.PlcReturn || exception.PlcReturnCode is not byte code)
            {
                throw new VirtualPlcFailureException(
                    stage,
                    $"期望 PLC 返回码 0x{expectedCode:X2}，实际 {exception.Category}/{exception.ErrorCode}。");
            }

            if (code != expectedCode)
            {
                throw new VirtualPlcFailureException(
                    stage,
                    $"期望 PLC 返回码 0x{expectedCode:X2}，实际 0x{code:X2}（{exception.ErrorCode}）。");
            }

            return code;
        }
    }

    private void VerifyPortReleased()
    {
        var listener = new TcpListener(IPAddress.Parse(_settings.Host), _settings.Port);

        try
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    listener.Start();
                    return;
                }
                catch (SocketException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }

            throw new VirtualPlcFailureException(
                "端口释放",
                $"服务器停止后端口 {_settings.Port} 仍被监听。");
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch (SocketException)
            {
            }
        }
    }

    private static bool IsPortInUse(string errorText)
    {
        return errorText.Contains("in use", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("Address", StringComparison.OrdinalIgnoreCase)
            || errorText.Contains("10048", StringComparison.Ordinal)
            || errorText.Contains("bind", StringComparison.OrdinalIgnoreCase);
    }

    private static void Cleanup(S7Server? server, VirtualPlcRegionRegistry? regions)
    {
        if (server is null)
        {
            return;
        }

        try
        {
            _ = server.Stop();
        }
        catch
        {
        }

        if (regions is not null)
        {
            try
            {
                regions.UnregisterAll(server);
            }
            catch
            {
            }
        }

        try
        {
            NativeServerInterop.Destroy(server);
        }
        catch
        {
        }
    }
}