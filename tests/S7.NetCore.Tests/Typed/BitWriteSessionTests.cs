using S7.NetCore;
using S7.NetCore.Tests.VirtualPlc;

namespace S7.NetCore.Tests.Typed;

public sealed class BitWriteSessionTests
{
    [Fact]
    public async Task BitWrite_MutatesOnlyTargetBitWithOneFrame()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0b1010_0101;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int writeCountBefore = transport.WriteCount;

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)));

        Assert.Equal(1, written);
        Assert.Equal(0b1010_1101, transport.Memory[0]);
        Assert.Equal(writeCountBefore + 1, transport.WriteCount);

        BitWriteBoundary[] bitWrites = ChunkFrameInspector.BitWrites(transport.ReceivedFrames).ToArray();
        Assert.Single(bitWrites);
        Assert.Equal(0, bitWrites[0].StartByte);
        Assert.Equal(3, bitWrites[0].BitNumber);
        Assert.True(bitWrites[0].Value);
        Assert.Empty(ChunkFrameInspector.WriteChunks(transport.ReceivedFrames));

        written = await client.WriteValueAsync(
            new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(false)));

        Assert.Equal(1, written);
        Assert.Equal(0b1010_0101, transport.Memory[0]);
    }

    [Fact]
    public async Task BitWrite_PlcError_KeepsReturnCode()
    {
        var transport = new FakeTcpTransport();
        transport.WriteReturnCodeProvider = _ => 0x06;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true))));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcDataTypeNotSupported, exception.ErrorCode);
        Assert.Equal((byte)0x06, exception.PlcReturnCode);
        Assert.Equal(0, exception.BytesWritten);
        Assert.Null(exception.WrittenRange);
    }

    [Fact]
    public async Task BitWrite_Timeout_KeepsConnectedAndUncertain()
    {
        var options = CreateOptions() with { WriteTimeout = TimeSpan.FromSeconds(1) };
        var transport = new FakeTcpTransport();
        await using S7Client client = new(options, transport: transport);
        await client.ConnectAsync();

        transport.RequestInterceptor = request => request.Length > 17 && request[17] == 0x05
            ? null
            : transport.DefaultResponseForTest(request);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true))));

        Assert.Equal(S7ErrorCategory.Timeout, exception.Category);
        Assert.Equal(S7ErrorCode.WriteTimeout, exception.ErrorCode);
        Assert.True(exception.WriteOutcomeUncertain);
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    [Fact]
    public async Task BitWrite_Cancelled_DoesNotSendFrame()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int writeCountBefore = transport.WriteCount;

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)),
                cancellationSource.Token));

        Assert.Equal(S7ErrorCategory.Cancelled, exception.Category);
        Assert.Equal(S7ErrorCode.OperationCancelled, exception.ErrorCode);
        Assert.Equal(writeCountBefore, transport.WriteCount);
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    [Fact]
    public async Task BitWrite_RemainsReadableAfterSuccess()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0b0000_0000;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        _ = await client.WriteValueAsync(
            new S7TypedWriteRequest("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)));

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBB0", S7DataType.Byte));

        Assert.Equal(0b0000_1000, result.Value.AsByte());
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    private static S7Client CreateClient(FakeTcpTransport transport)
    {
        return new S7Client(CreateOptions(), transport: transport);
    }

    private static S7ConnectionOptions CreateOptions()
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Slot = 1,
            RequestedPduSize = 480
        };
    }
}