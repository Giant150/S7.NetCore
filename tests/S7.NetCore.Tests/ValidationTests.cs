using S7.NetCore;

namespace S7.NetCore.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-an-ip")]
    [InlineData("127.0.0.999")]
    [InlineData("host.local")]
    public void InvalidIpAddress_IsRejected(string address)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions(address)));

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(S7ErrorCode.InvalidAddress, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void InvalidPort_IsRejected(int port)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions() with { Port = port }));

        Assert.Equal(S7ErrorCode.InvalidPort, exception.ErrorCode);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(16, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 16)]
    public void InvalidRackOrSlot_IsRejected(int rack, int slot)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions() with { Rack = rack, Slot = slot }));

        Assert.Equal(S7ErrorCode.InvalidRackSlot, exception.ErrorCode);
    }

    [Fact]
    public void UndefinedCpuType_IsRejected()
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions() with { CpuType = (S7CpuType)99 }));

        Assert.Equal(S7ErrorCode.InvalidCpuType, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void InvalidConnectTimeout_IsRejected(int seconds)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions() with { ConnectTimeout = TimeSpan.FromSeconds(seconds) }));

        Assert.Equal(S7ErrorCode.InvalidTimeout, exception.ErrorCode);
    }

    [Theory]
    [InlineData(239)]
    [InlineData(961)]
    public void InvalidRequestedPduSize_IsRejected(int pduSize)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => _ = new S7Client(CreateOptions() with { RequestedPduSize = pduSize }));

        Assert.Equal(S7ErrorCode.InvalidPduSize, exception.ErrorCode);
    }

    [Fact]
    public void NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new S7Client(null!));
    }

    [Fact]
    public void Read_DataBlockWithoutDbNumber_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 0, 0, 1)); });

        Assert.Equal(S7ErrorCode.InvalidDbNumber, exception.ErrorCode);
    }

    [Fact]
    public void Read_NonDataBlockWithDbNumber_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 1, 0, 1)); });

        Assert.Equal(S7ErrorCode.InvalidDbNumber, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Read_InvalidLength_IsRejected(int length)
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, length)); });

        Assert.Equal(S7ErrorCode.InvalidLength, exception.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Read_InvalidStartByte_IsRejected(int startByte)
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, startByte, 1)); });

        Assert.Equal(S7ErrorCode.InvalidStartByte, exception.ErrorCode);
    }

    [Fact]
    public void Read_UnsupportedArea_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.ReadBytesAsync(new S7ReadRequest((S7Area)0x99, 0, 0, 1)); });

        Assert.Equal(S7ErrorCode.AreaNotSupported, exception.ErrorCode);
    }

    [Fact]
    public void Write_InputArea_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.WriteBytesAsync(new S7WriteRequest(S7Area.Input, 0, 0, new byte[] { 1 })); });

        Assert.Equal(S7ErrorCode.AreaNotSupported, exception.ErrorCode);
    }

    [Fact]
    public void Write_EmptyData_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.WriteBytesAsync(new S7WriteRequest(S7Area.Memory, 0, 0, ReadOnlyMemory<byte>.Empty)); });

        Assert.Equal(S7ErrorCode.InvalidData, exception.ErrorCode);
    }

    [Fact]
    public void Write_DataOver65535_IsRejected()
    {
        S7Client client = CreateClient();

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.WriteBytesAsync(new S7WriteRequest(S7Area.Memory, 0, 0, new byte[65536])); });

        Assert.Equal(S7ErrorCode.InvalidData, exception.ErrorCode);
    }

    private static S7ConnectionOptions CreateOptions(string address = "127.0.0.1")
    {
        return new S7ConnectionOptions
        {
            IpAddress = address,
            CpuType = S7CpuType.S71200,
            Slot = 1
        };
    }

    private static S7Client CreateClient()
    {
        return new S7Client(CreateOptions());
    }
}