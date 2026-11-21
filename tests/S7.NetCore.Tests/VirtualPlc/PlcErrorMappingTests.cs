using System.Net;
using System.Net.Sockets;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class PlcErrorMappingTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task V_E01_Db1_ReadBeyondCapacity_ReturnsAddressOutOfRange()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 65535, 2),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, exception.ErrorCode);
        Assert.Equal((byte)0x05, exception.PlcReturnCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
    }

    [VirtualPlcFact]
    public async Task V_E02_Db4_Read_ReturnsObjectNotExist()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 4, 0, 2),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, exception.ErrorCode);
        Assert.Equal((byte)0x0A, exception.PlcReturnCode);
    }

    [VirtualPlcFact]
    public async Task V_E03_Db4_Write_ReturnsObjectNotExistWithoutProgress()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.WriteBytesAsync(
                new S7WriteRequest(S7Area.DataBlock, 4, 0, new byte[2]),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, exception.ErrorCode);
        Assert.Equal((byte)0x0A, exception.PlcReturnCode);
        Assert.Equal(0, exception.BytesWritten);
        Assert.Null(exception.WrittenRange);
    }
}

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class ConnectionFailureTests
{
    [Fact]
    public async Task V_E04_ConnectToClosedPort_FailsWithConnectionError()
    {
        int freePort = GetFreePort();
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            Port = freePort,
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        await using var client = new S7Client(options);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ConnectAsync());

        Assert.Equal(S7ErrorCategory.Connection, exception.Category);
        Assert.Equal(S7ErrorCode.TcpConnectFailed, exception.ErrorCode);
        Assert.Equal(S7SessionState.Disconnected, client.State);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}