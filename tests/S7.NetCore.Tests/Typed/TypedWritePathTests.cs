using System.Text;
using S7.NetCore;
using S7.NetCore.Tests.VirtualPlc;

namespace S7.NetCore.Tests.Typed;

public sealed class TypedWritePathTests
{
    [Fact]
    public async Task WriteInt_EncodesBigEndian()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest("DB2.DBW0", S7DataType.Int, S7Value.FromInt16(-12345)));

        Assert.Equal(2, written);
        Assert.Equal(0xCF, transport.Memory[0]);
        Assert.Equal(0xC7, transport.Memory[1]);
    }

    [Fact]
    public async Task WriteS7String_WritesHeaderAndPadsZero()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBB16",
                S7DataType.S7String,
                S7Value.FromString("S7-PLC"),
                Capacity: 16));

        Assert.Equal(18, written);
        Assert.Equal(16, transport.Memory[16]);
        Assert.Equal(6, transport.Memory[17]);
        Assert.Equal("S7-PLC"u8.ToArray(), transport.Memory[18..24]);
        Assert.Equal(0, transport.Memory[24]);
    }

    [Fact]
    public async Task WriteCString_WritesTerminatorAndPadsZero()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBB40",
                S7DataType.String,
                S7Value.FromString("HELLO"),
                Capacity: 8));

        Assert.Equal(8, written);
        Assert.Equal("HELLO"u8.ToArray(), transport.Memory[40..45]);
        Assert.Equal(0, transport.Memory[45]);
        Assert.Equal(0, transport.Memory[46]);
        Assert.Equal(0, transport.Memory[47]);
    }

    [Fact]
    public async Task Write252Bytes_SplitsAcrossPdu()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBB8192",
                S7DataType.S7String,
                S7Value.FromString("HELLO"),
                Capacity: 250));

        Assert.Equal(252, written);
        Assert.Equal(250, transport.Memory[8192]);
        Assert.Equal(5, transport.Memory[8193]);
        Assert.Equal("HELLO"u8.ToArray(), transport.Memory[8194..8199]);

        ChunkBoundary[] chunks = ChunkFrameInspector.WriteChunks(transport.ReceivedFrames).ToArray();
        Assert.Equal(2, chunks.Length);
        Assert.Equal([8192, 8397], chunks.Select(chunk => chunk.StartByte).ToArray());
        Assert.Equal([205, 47], chunks.Select(chunk => chunk.Length).ToArray());
    }

    [Fact]
    public async Task Write_PlcErrorInSecondChunk_ReportsProgress()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        transport.WriteReturnCodeProvider = startByte => startByte == 8397 ? (byte)0x06 : (byte)0xFF;
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBB8192",
                    S7DataType.S7String,
                    S7Value.FromString("HELLO"),
                    Capacity: 250)));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcDataTypeNotSupported, exception.ErrorCode);
        Assert.Equal((byte)0x06, exception.PlcReturnCode);
        Assert.Equal(205, exception.BytesWritten);
        Assert.Equal(new WrittenRange(8192, 205), exception.WrittenRange);
    }

    [Fact]
    public async Task Write_InputArea_IsRejectedWithoutFrames()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int writeCountBefore = transport.WriteCount;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest("IB0", S7DataType.Byte, S7Value.FromByte(1))));

        Assert.Equal(S7ErrorCode.AreaNotSupported, exception.ErrorCode);
        Assert.Equal(writeCountBefore, transport.WriteCount);
    }

    [Fact]
    public async Task Write_InvalidValue_IsRejectedWithoutFrames()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int writeCountBefore = transport.WriteCount;

        Assert.Equal(
            S7ErrorCode.ValueOutOfRange,
            (await Assert.ThrowsAsync<S7ClientException>(
                () => client.WriteValueAsync(
                    new S7TypedWriteRequest("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(256))))).ErrorCode);

        Assert.Equal(
            S7ErrorCode.StringEncodingInvalid,
            (await Assert.ThrowsAsync<S7ClientException>(
                () => client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB1.DBB0",
                        S7DataType.String,
                        S7Value.FromString("PLČ"),
                        Capacity: 8)))).ErrorCode);

        Assert.Equal(
            S7ErrorCode.StringCapacityInvalid,
            (await Assert.ThrowsAsync<S7ClientException>(
                () => client.WriteValueAsync(
                    new S7TypedWriteRequest(
                        "DB1.DBB0",
                        S7DataType.String,
                        S7Value.FromString("ABCDE"),
                        Capacity: 4)))).ErrorCode);

        Assert.Equal(writeCountBefore, transport.WriteCount);
    }

    [Fact]
    public async Task Write_LogContainsTypeContextButNoValue()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using S7Client client = new(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        _ = await client.WriteValueAsync(
            new S7TypedWriteRequest("DB2.DBW0", S7DataType.Int, S7Value.FromInt16(-12345)));

        S7LogEntry entry = logger.Records
            .Where(record => record.Level == S7LogLevel.Information
                && record.Entry.Operation == "Write"
                && record.Entry.AddressExpression is not null)
            .Select(record => record.Entry)
            .Single();

        Assert.Equal("DB2.DBW0", entry.AddressExpression);
        Assert.Equal(S7DataType.Int, entry.DataType);
        Assert.Equal(2, entry.BytesOccupied);
        Assert.Null(entry.ProtocolTrace);
        Assert.DoesNotContain("12345", entry.AddressExpression!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteS7String_ThenReadBack_RoundTrips()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        _ = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBB16",
                S7DataType.S7String,
                S7Value.FromString("S7-PLC"),
                Capacity: 16));

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBB16", S7DataType.S7String, Capacity: 16));

        Assert.Equal("S7-PLC", result.Value.AsString());
        Assert.Equal(Encoding.ASCII.GetString(transport.Memory[18..24]), result.Value.AsString());
    }

    [Fact]
    public async Task WriteS7String_WithCapacitySuffix_UsesAddressCapacity()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBS16.16",
                S7DataType.S7String,
                S7Value.FromString("S7-PLC")));

        Assert.Equal(18, written);
        Assert.Equal(16, transport.Memory[16]);
        Assert.Equal(6, transport.Memory[17]);
        Assert.Equal("S7-PLC"u8.ToArray(), transport.Memory[18..24]);
        Assert.Equal(0, transport.Memory[24]);

        ChunkBoundary chunk = Assert.Single(ChunkFrameInspector.WriteChunks(transport.ReceivedFrames));
        Assert.Equal(16, chunk.StartByte);
        Assert.Equal(18, chunk.Length);
    }

    [Fact]
    public async Task WriteString_SuffixCapacityConflict_Reports136WithoutWriteRequest()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int framesBefore = transport.ReceivedFrames.Count;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBR16.16",
                    S7DataType.String,
                    S7Value.FromString("A"),
                    Capacity: 8)));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.Equal(framesBefore, transport.ReceivedFrames.Count);
        Assert.Contains("不一致", exception.Message, StringComparison.Ordinal);
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