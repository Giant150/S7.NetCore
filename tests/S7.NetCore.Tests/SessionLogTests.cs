using S7.NetCore;

namespace S7.NetCore.Tests;

public sealed class SessionLogTests
{
    [Fact]
    public async Task Read_WritesDebugChunkEntries()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        FillMemory(transport.Memory, 300);
        var logger = new TestLogger();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        _ = await client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 300));

        S7LogEntry[] chunks = logger.Records
            .Where(record => record.Level == S7LogLevel.Debug && record.Entry.Operation == "Read")
            .Select(record => record.Entry)
            .OrderBy(entry => entry.ChunkIndex)
            .ToArray();

        Assert.Equal(2, chunks.Length);
        Assert.Equal(1, chunks[0].ChunkIndex);
        Assert.Equal(2, chunks[0].ChunkCount);
        Assert.Equal(0, chunks[0].StartByte);
        Assert.Equal(222, chunks[0].Length);
        Assert.Equal(222, chunks[1].StartByte);
        Assert.Equal(78, chunks[1].Length);
        Assert.Equal(S7Area.DataBlock, chunks[0].Area);
        Assert.Equal(1, chunks[0].DbNumber);
        Assert.True(chunks[0].Succeeded);

        S7LogEntry summary = logger.Records
            .Where(record => record.Level == S7LogLevel.Information && record.Entry.Operation == "Read")
            .Select(record => record.Entry)
            .Single();

        Assert.Null(summary.ChunkIndex);
    }

    [Fact]
    public async Task Write_WritesDebugChunkEntries()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        var logger = new TestLogger();
        await using var client = new S7Client(CreateOptions(), logger, transport);
        await client.ConnectAsync();

        int written = await client.WriteBytesAsync(
            new S7WriteRequest(S7Area.DataBlock, 2, 0, new byte[250]));

        Assert.Equal(250, written);

        S7LogEntry[] chunks = logger.Records
            .Where(record => record.Level == S7LogLevel.Debug && record.Entry.Operation == "Write")
            .Select(record => record.Entry)
            .OrderBy(entry => entry.ChunkIndex)
            .ToArray();

        Assert.Equal(2, chunks.Length);
        Assert.Equal(1, chunks[0].ChunkIndex);
        Assert.Equal(2, chunks[0].ChunkCount);
        Assert.Equal(0, chunks[0].StartByte);
        Assert.Equal(205, chunks[0].Length);
        Assert.Equal(205, chunks[1].StartByte);
        Assert.Equal(45, chunks[1].Length);

        S7LogEntry summary = logger.Records
            .Where(record => record.Level == S7LogLevel.Information && record.Entry.Operation == "Write")
            .Select(record => record.Entry)
            .Single();

        Assert.Null(summary.ChunkIndex);
    }

    [Fact]
    public void NullLogger_DebugIsDisabled()
    {
        Assert.False(NullS7Logger.Instance.IsEnabled(S7LogLevel.Debug));
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

    private static void FillMemory(byte[] memory, int length)
    {
        for (int i = 0; i < length; i++)
        {
            memory[i] = (byte)((i * 7 + 11) & 0xFF);
        }
    }
}