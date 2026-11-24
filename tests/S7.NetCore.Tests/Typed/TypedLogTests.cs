using S7.NetCore;

namespace S7.NetCore.Tests.Typed;

public sealed class TypedLogTests
{
    [Fact]
    public async Task WStringReadSuccess_LogsCapacityAndOccupation()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        PrepareWStringMemory(transport.Memory);
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBB0", S7DataType.S7WString, 16));

        Assert.Equal("温度A1", result.Value.AsString());

        S7LogEntry entry = FindEntry(logger, "Read", succeeded: true);
        Assert.Equal(16, entry.Capacity);
        Assert.Equal(36, entry.BytesOccupied);
        Assert.Equal(36, entry.Length);
        Assert.Null(entry.ChunkIndex);
        Assert.DoesNotContain("温度", entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WStringWriteSuccess_LogsCapacity()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        int written = await client.WriteValueAsync(
            new S7TypedWriteRequest(
                "DB2.DBB64",
                S7DataType.S7WString,
                S7Value.FromString("温度A1"),
                16));

        Assert.Equal(36, written);
        S7LogEntry entry = FindEntry(logger, "Write", succeeded: true);
        Assert.Equal(16, entry.Capacity);
        Assert.Equal(36, entry.BytesOccupied);
    }

    [Fact]
    public async Task WStringWriteSurrogateFailure_LogsValidateEntry()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBB64",
                    S7DataType.S7WString,
                    S7Value.FromString("\uD83D"),
                    16)));

        Assert.Equal(S7ErrorCode.StringEncodingInvalid, exception.ErrorCode);
        S7LogEntry entry = FindEntry(logger, "Write", succeeded: false);
        Assert.Equal(S7OperationStage.Validate, entry.Stage);
        Assert.Equal(S7ErrorCode.StringEncodingInvalid, entry.ErrorCode);
        Assert.Equal(16, entry.Capacity);
        Assert.Equal(36, entry.BytesOccupied);
        Assert.DoesNotContain("\uD83D", entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WStringWriteCapacityFailure_LogsCapacity()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBB64",
                    S7DataType.S7WString,
                    S7Value.FromString("ABCDE"),
                    4)));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.DoesNotContain("ABCDE", exception.Message, StringComparison.Ordinal);

        S7LogEntry entry = FindEntry(logger, "Write", succeeded: false);
        Assert.Equal(4, entry.Capacity);
        Assert.Equal(12, entry.BytesOccupied);
        Assert.Equal(S7OperationStage.Validate, entry.Stage);
    }

    [Fact]
    public async Task WStringHeaderFailure_Logs408()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0x00;
        transport.Memory[1] = 0x10;
        transport.Memory[2] = 0x00;
        transport.Memory[3] = 0x11;
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB0", S7DataType.S7WString, 16)));

        Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, exception.ErrorCode);
        S7LogEntry entry = FindEntry(logger, "Read", succeeded: false);
        Assert.Equal(S7OperationStage.Read, entry.Stage);
        Assert.Equal(S7ErrorCode.InvalidS7WStringHeader, entry.ErrorCode);
        Assert.Equal(16, entry.Capacity);
        Assert.Equal(36, entry.BytesOccupied);
    }

    [Fact]
    public async Task WStringDataFailure_Logs409()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        transport.Memory[0] = 0x00;
        transport.Memory[1] = 0x10;
        transport.Memory[2] = 0x00;
        transport.Memory[3] = 0x01;
        transport.Memory[4] = 0xDE;
        transport.Memory[5] = 0x00;
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB0", S7DataType.S7WString, 16)));

        Assert.Equal(S7ErrorCode.InvalidS7WStringData, exception.ErrorCode);
        S7LogEntry entry = FindEntry(logger, "Read", succeeded: false);
        Assert.Equal(S7ErrorCode.InvalidS7WStringData, entry.ErrorCode);
        Assert.Equal(16, entry.Capacity);
        Assert.Equal(36, entry.BytesOccupied);
    }

    [Fact]
    public async Task NonStringType_LogsNullCapacity()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        _ = await client.ReadValueAsync(
            new S7TypedReadRequest("DB2.DBW0", S7DataType.Word));

        S7LogEntry entry = FindEntry(logger, "Read", succeeded: true, S7DataType.Word);
        Assert.Null(entry.Capacity);
        Assert.Equal(2, entry.BytesOccupied);
    }

    [Fact]
    public async Task StringSuffixReadSuccess_LogsAddressCapacity()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        "HELLO"u8.ToArray().CopyTo(transport.Memory, 13);
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBR13.15", S7DataType.String));

        Assert.Equal("HELLO", result.Value.AsString());
        S7LogEntry entry = FindEntry(logger, "Read", succeeded: true, S7DataType.String);
        Assert.Equal(15, entry.Capacity);
        Assert.Equal(15, entry.BytesOccupied);
        Assert.Equal(13, entry.StartByte);
    }

    [Fact]
    public async Task StringSuffixValidationFailure_LogsEffectiveCapacity()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(
                new S7TypedReadRequest("DB1.DBR13.15", S7DataType.S7String)));

        S7LogEntry entry = FindEntry(logger, "Read", succeeded: false, S7DataType.S7String);
        Assert.Equal(S7ErrorCode.DataTypeAddressMismatch, entry.ErrorCode);
        Assert.Equal(15, entry.Capacity);
        Assert.Equal(17, entry.BytesOccupied);
    }

    [Fact]
    public async Task UnsupportedDataTypeFailure_LogsZeroOccupiedBytes()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", (S7DataType)99)));

        S7LogEntry entry = FindEntry(logger, "Read", succeeded: false, (S7DataType)99);
        Assert.Equal(S7ErrorCode.DataTypeNotSupported, entry.ErrorCode);
        Assert.Equal(0, entry.BytesOccupied);
        Assert.Null(entry.Capacity);
    }

    [Fact]
    public async Task LRealWidthMismatchFailure_LogsEightOccupiedBytes()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", S7DataType.LReal)));

        S7LogEntry entry = FindEntry(logger, "Read", succeeded: false, S7DataType.LReal);
        Assert.Equal(S7ErrorCode.DataTypeAddressMismatch, entry.ErrorCode);
        Assert.Equal(8, entry.BytesOccupied);
        Assert.Null(entry.Capacity);
    }

    private static S7LogEntry FindEntry(
        TestLogger logger,
        string operation,
        bool succeeded,
        S7DataType? dataType = null)
    {
        return logger.Records
            .Where(record => record.Entry.Operation == operation
                && record.Entry.Succeeded == succeeded
                && record.Entry.AddressExpression is not null
                && (dataType is null || record.Entry.DataType == dataType))
            .Select(record => record.Entry)
            .Last();
    }

    private static void PrepareWStringMemory(byte[] memory)
    {
        const string text = "温度A1";
        memory[0] = 0x00;
        memory[1] = 0x10;
        memory[2] = 0x00;
        memory[3] = 0x04;

        for (int i = 0; i < text.Length; i++)
        {
            memory[4 + (i * 2)] = (byte)(text[i] >> 8);
            memory[5 + (i * 2)] = (byte)text[i];
        }
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