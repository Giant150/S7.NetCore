using System.Text;
using S7.NetCore;
using S7.NetCore.Tests.VirtualPlc;

namespace S7.NetCore.Tests.Typed;

public sealed class TypedReadPathTests
{
    [Fact]
    public async Task ReadWord_ReturnsBigEndianValue()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0x12;
        transport.Memory[1] = 0x34;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Word));

        Assert.Equal((ushort)0x1234, result.Value.AsUInt16());
        Assert.Equal(2, result.BytesOccupied);
        Assert.Equal(S7Area.DataBlock, result.Address.Area);
        Assert.Equal(1, result.Address.DbNumber);
        Assert.Equal(0, result.Address.StartByte);
    }

    [Fact]
    public async Task ReadInt_ReadsSameBytesAsSignedView()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0xCF;
        transport.Memory[1] = 0xC7;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Int));

        Assert.Equal((short)-12345, result.Value.AsInt16());
        Assert.Equal(2, result.BytesOccupied);

        S7TypedReadResult word = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Word));
        Assert.Equal((ushort)53191, word.Value.AsUInt16());
    }

    [Fact]
    public async Task ReadBool_ReadsSingleByte()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0b0000_1000;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBX0.3", S7DataType.Bool));

        Assert.True(result.Value.AsBoolean());
        Assert.Equal(1, result.BytesOccupied);

        IReadOnlyList<ChunkBoundary> chunks = ChunkFrameInspector.ReadChunks(transport.ReceivedFrames);
        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].Length);
    }

    [Fact]
    public async Task ReadS7String_DecodesHeaderAndContent()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 16;
        transport.Memory[1] = 5;
        Encoding.ASCII.GetBytes("HELLO").CopyTo(transport.Memory, 2);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBB0", S7DataType.S7String, Capacity: 16));

        Assert.Equal("HELLO", result.Value.AsString());
        Assert.Equal(18, result.BytesOccupied);
    }

    [Fact]
    public async Task Read252Bytes_SplitsAcrossPdu()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        transport.Memory[8192] = 250;
        transport.Memory[8193] = 5;
        Encoding.ASCII.GetBytes("HELLO").CopyTo(transport.Memory, 8194);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBB8192", S7DataType.S7String, Capacity: 250));

        Assert.Equal("HELLO", result.Value.AsString());
        Assert.Equal(252, result.BytesOccupied);

        ChunkBoundary[] chunks = ChunkFrameInspector.ReadChunks(transport.ReceivedFrames).ToArray();
        Assert.Equal(2, chunks.Length);
        Assert.Equal([8192, 8414], chunks.Select(chunk => chunk.StartByte).ToArray());
        Assert.Equal([222, 30], chunks.Select(chunk => chunk.Length).ToArray());
    }

    [Fact]
    public async Task Read_PlcErrorInSecondChunk_ThrowsWithoutValue()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        transport.Memory[8192] = 250;
        transport.Memory[8193] = 5;
        Encoding.ASCII.GetBytes("HELLO").CopyTo(transport.Memory, 8194);
        transport.ReadReturnCodeProvider = startByte => startByte == 8414 ? (byte)0x05 : (byte)0xFF;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB8192", S7DataType.S7String, Capacity: 250)));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, exception.ErrorCode);
        Assert.Equal((byte)0x05, exception.PlcReturnCode);
        Assert.Contains("DB2.DBB8192", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_TruncatedResponse_Throws404()
    {
        var transport = new FakeTcpTransport { TruncateReadBytes = 1 };
        transport.Memory[0] = 0x12;
        transport.Memory[1] = 0x34;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", S7DataType.Word)));

        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
        Assert.Equal(S7ErrorCode.ResponseTooShort, exception.ErrorCode);
    }

    [Fact]
    public async Task Read_InvalidS7StringHeader_Throws406()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 16;
        transport.Memory[1] = 17;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB0", S7DataType.S7String, Capacity: 16)));

        Assert.Equal(S7ErrorCode.InvalidS7StringHeader, exception.ErrorCode);
        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
    }

    [Fact]
    public async Task Read_NonAsciiContent_Throws407()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0x80;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB0", S7DataType.String, Capacity: 8)));

        Assert.Equal(S7ErrorCode.NonAsciiStringData, exception.ErrorCode);
        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
    }

    [Fact]
    public async Task Read_WhenNotConnected_ThrowsWithoutFrames()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", S7DataType.Int)));

        Assert.Equal(S7ErrorCode.SessionNotEstablished, exception.ErrorCode);
        Assert.Empty(transport.ReceivedFrames);
    }

    [Fact]
    public async Task Read_LogContainsTypeContextButNoValue()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0x12;
        transport.Memory[1] = 0x34;
        await using S7Client client = new(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        _ = await client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", S7DataType.Word));

        S7LogEntry entry = logger.Records
            .Where(record => record.Level == S7LogLevel.Information
                && record.Entry.Operation == "Read"
                && record.Entry.AddressExpression is not null)
            .Select(record => record.Entry)
            .Single();

        Assert.Equal("DB1.DBW0", entry.AddressExpression);
        Assert.Equal(S7DataType.Word, entry.DataType);
        Assert.Equal(2, entry.BytesOccupied);
        Assert.Null(entry.ChunkIndex);
        Assert.Null(entry.ProtocolTrace);
    }

    [Fact]
    public async Task ReadString_WithCapacitySuffix_UsesAddressCapacity()
    {
        var transport = new FakeTcpTransport();
        "HELLO"u8.ToArray().CopyTo(transport.Memory, 13);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBR13.15", S7DataType.String));

        Assert.Equal("HELLO", result.Value.AsString());
        Assert.Equal(15, result.BytesOccupied);
        Assert.Equal(15, result.EffectiveCapacity);
        Assert.Equal(S7DataType.String, result.DataType);
        Assert.Equal(S7AddressSuffix.R, result.Address.Suffix);
        Assert.Equal(15, result.Address.AddressCapacity);

        ChunkBoundary chunk = Assert.Single(ChunkFrameInspector.ReadChunks(transport.ReceivedFrames));
        Assert.Equal(13, chunk.StartByte);
        Assert.Equal(15, chunk.Length);
    }

    [Fact]
    public async Task ReadWString_WithCapacitySuffix_DecodesWideContent()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[13] = 0x00;
        transport.Memory[14] = 0x0F;
        transport.Memory[15] = 0x00;
        transport.Memory[16] = 0x05;
        Encoding.BigEndianUnicode.GetBytes("HELLO").CopyTo(transport.Memory, 17);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBL13.15", S7DataType.S7WString));

        Assert.Equal("HELLO", result.Value.AsString());
        Assert.Equal(34, result.BytesOccupied);
        Assert.Equal(15, result.EffectiveCapacity);
        Assert.Equal(S7DataType.S7WString, result.DataType);
        Assert.Equal(S7AddressSuffix.L, result.Address.Suffix);
    }

    [Fact]
    public async Task ReadString_WithSuffix_DecodeFailure_AppendsCapacityContext()
    {
        var transport = new FakeTcpTransport();
        transport.Memory[13] = 0x80;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(new S7TypedReadRequest("DB1.DBR13.15", S7DataType.String)));

        Assert.Equal(S7ErrorCode.NonAsciiStringData, exception.ErrorCode);
        Assert.Contains("DB1.DBR13.15", exception.Message, StringComparison.Ordinal);
        Assert.Contains("后缀 R", exception.Message, StringComparison.Ordinal);
        Assert.Contains("地址容量 15", exception.Message, StringComparison.Ordinal);
        Assert.Contains("有效容量 15", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ASCII 字节", exception.Message, StringComparison.Ordinal);
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