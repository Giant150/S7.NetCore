using System.Net;
using System.Net.Sockets;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
public sealed class VirtualPlcEnvironmentTests
{
    [Fact]
    public void DefaultEndpoint_IsLoopbackAndExpected()
    {
        VirtualPlcEndpoint endpoint = VirtualPlcEndpoint.Default;

        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.Equal(1102, endpoint.Port);
        Assert.Equal(S7CpuType.S71200, endpoint.CpuType);
        Assert.Equal(0, endpoint.Rack);
        Assert.Equal(1, endpoint.Slot);
        Assert.True(endpoint.IsLoopback);

        S7ConnectionOptions options = endpoint.ToOptions();
        Assert.Equal(480, options.RequestedPduSize);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ReadTimeout);
    }

    [Fact]
    public void FromEnvironment_OverridesDefaults()
    {
        using var scope = new EnvironmentVariableScope();
        scope.Set("S7_VIRTUAL_PLC_HOST", "192.168.1.50");
        scope.Set("S7_VIRTUAL_PLC_PORT", "1102");
        scope.Set("S7_VIRTUAL_PLC_CPU", "S7300");
        scope.Set("S7_VIRTUAL_PLC_RACK", "1");
        scope.Set("S7_VIRTUAL_PLC_SLOT", "2");

        VirtualPlcEndpoint endpoint = VirtualPlcEndpoint.FromEnvironment();

        Assert.Equal("192.168.1.50", endpoint.Host);
        Assert.Equal(1102, endpoint.Port);
        Assert.Equal(S7CpuType.S7300, endpoint.CpuType);
        Assert.Equal(1, endpoint.Rack);
        Assert.Equal(2, endpoint.Slot);
        Assert.False(endpoint.IsLoopback);
    }

    [Theory]
    [InlineData("S7_VIRTUAL_PLC_HOST", "not-an-ip")]
    [InlineData("S7_VIRTUAL_PLC_PORT", "0")]
    [InlineData("S7_VIRTUAL_PLC_PORT", "102")]
    [InlineData("S7_VIRTUAL_PLC_PORT", "1023")]
    [InlineData("S7_VIRTUAL_PLC_PORT", "65536")]
    [InlineData("S7_VIRTUAL_PLC_CPU", "S7999")]
    [InlineData("S7_VIRTUAL_PLC_RACK", "16")]
    [InlineData("S7_VIRTUAL_PLC_SLOT", "-1")]
    public void FromEnvironment_InvalidValue_Throws(string name, string value)
    {
        using var scope = new EnvironmentVariableScope();
        scope.Set(name, value);

        Assert.Throws<FormatException>(() => VirtualPlcEndpoint.FromEnvironment());
    }

    [Fact]
    [Trait("Category", "VirtualPlc")]
    public async Task V_N01_ProbeUnavailableEndpoint_ReportsNotReady()
    {
        int freePort = GetFreePort();
        var endpoint = new VirtualPlcEndpoint("127.0.0.1", freePort, S7CpuType.S71200, 0, 1);

        VirtualPlcProbe probe = await VirtualPlcEnvironment.ProbeAsync(endpoint, CancellationToken.None);

        Assert.False(probe.Ready);
        Assert.Equal(S7OperationStage.Connect, probe.Stage);
        Assert.False(string.IsNullOrWhiteSpace(probe.Reason));
    }

    [Fact]
    public async Task RequireReadyAsync_NoListener_ThrowsSkip()
    {
        using var scope = new EnvironmentVariableScope();
        scope.Set("S7_VIRTUAL_PLC_HOST", "127.0.0.1");
        scope.Set("S7_VIRTUAL_PLC_PORT", GetFreePort().ToString());

        Xunit.Sdk.SkipException exception = await Assert.ThrowsAsync<Xunit.Sdk.SkipException>(
            () => VirtualPlcEnvironment.RequireReadyAsync(CancellationToken.None));

        Assert.Contains("虚拟 PLC 未就绪", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowNonLoopbackWrites_ReadsOptInFlag()
    {
        using var scope = new EnvironmentVariableScope();
        scope.Set("S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK", "TRUE");

        Assert.True(VirtualPlcEnvironment.AllowNonLoopbackWrites);

        scope.Set("S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK", "false");
        Assert.False(VirtualPlcEnvironment.AllowNonLoopbackWrites);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = [];

        internal void Set(string name, string? value)
        {
            if (!_originalValues.ContainsKey(name))
            {
                _originalValues[name] = Environment.GetEnvironmentVariable(name);
            }

            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}