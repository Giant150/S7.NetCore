using S7.NetCore;

namespace S7.NetCore.Tests;

public sealed class ClientIntegrationTests
{
    [Fact]
    public async Task Connect_NegotiatesPduAndSetsState()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 480 };
        await using S7Client client = CreateClient(transport);

        await client.ConnectAsync();

        Assert.Equal(S7SessionState.Connected, client.State);
        Assert.True(client.IsConnected);
        Assert.Equal(480, client.NegotiatedPduSize);
        Assert.Equal(2, transport.ReceivedFrames.Count);
        Assert.Equal(1, transport.ConnectedCount);
    }

    [Fact]
    public async Task Connect_SetupResponseWithoutPdu_FallsBackTo240()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 0 };
        await using S7Client client = CreateClient(transport);

        await client.ConnectAsync();

        Assert.Equal(240, client.NegotiatedPduSize);
    }

    [Fact]
    public async Task Read_1024Bytes_SpansMultiplePdusAndPreservesOrder()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 480 };
        FillMemory(transport.Memory, 2000);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 1024));

        Assert.Equal(1024, data.Length);
        Assert.Equal(transport.Memory.AsSpan(0, 1024).ToArray(), data.ToArray());

        byte[][] readFrames = transport.ReceivedFrames.Where(frame => frame.Length > 17 && frame[17] == 0x04).ToArray();
        Assert.Equal(3, readFrames.Length);
        Assert.Equal(0, GetStartByte(readFrames[0]));
        Assert.Equal(462, GetStartByte(readFrames[1]));
        Assert.Equal(924, GetStartByte(readFrames[2]));
    }

    [Fact]
    public async Task Read_65535Bytes_UsesAllChunks()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 240 };
        FillMemory(transport.Memory, 65536);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 65535));

        Assert.Equal(65535, data.Length);
        Assert.Equal(transport.Memory.AsSpan(0, 65535).ToArray(), data.ToArray());

        int readFrameCount = transport.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x04);
        Assert.Equal(296, readFrameCount);
    }

    [Theory]
    [InlineData(S7Area.Input, 0)]
    [InlineData(S7Area.Output, 0)]
    [InlineData(S7Area.Memory, 0)]
    [InlineData(S7Area.DataBlock, 1)]
    public async Task Read_AllSupportedAreas_Succeeds(S7Area area, int dbNumber)
    {
        var transport = new FakeTcpTransport();
        FillMemory(transport.Memory, 64);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(area, dbNumber, 8, 16));

        Assert.Equal(16, data.Length);
        Assert.Equal(transport.Memory.AsSpan(8, 16).ToArray(), data.ToArray());
    }

    [Fact]
    public async Task Write_ThenRead_RoundTripsBytes()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 480 };
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        byte[] payload = Enumerable.Range(0, 1024).Select(i => (byte)(i * 3)).ToArray();
        int written = await client.WriteBytesAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 100, payload));

        Assert.Equal(1024, written);
        Assert.Equal(payload, transport.Memory.AsSpan(100, 1024).ToArray());

        byte[][] writeFrames = transport.ReceivedFrames.Where(frame => frame.Length > 17 && frame[17] == 0x05).ToArray();
        Assert.Equal(3, writeFrames.Length);

        ReadOnlyMemory<byte> readBack = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 100, 1024));
        Assert.Equal(payload, readBack.ToArray());
    }

    [Fact]
    public async Task Read_PlcErrorInLaterChunk_ThrowsWithoutReturningPartialData()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 480 };
        FillMemory(transport.Memory, 2000);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        transport.ReadReturnCodeProvider = startByte => startByte == 462 ? (byte)0x05 : (byte)0xFF;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 1024)));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, exception.ErrorCode);
        Assert.Equal((byte)0x05, exception.PlcReturnCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
        Assert.Contains("区域 DataBlock", exception.Message, StringComparison.Ordinal);
        Assert.Contains("起始 462", exception.Message, StringComparison.Ordinal);
        Assert.Contains("长度 462", exception.Message, StringComparison.Ordinal);
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    [Fact]
    public async Task Read_TruncatedResponse_ThrowsProtocolError()
    {
        var transport = new FakeTcpTransport { TruncateReadBytes = 1 };
        FillMemory(transport.Memory, 64);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 16)));

        Assert.Equal(S7ErrorCategory.Protocol, exception.Category);
        Assert.Equal(S7ErrorCode.ResponseTooShort, exception.ErrorCode);
    }

    [Fact]
    public async Task Write_PlcErrorInLaterChunk_ReportsWrittenRange()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 480 };
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        transport.WriteReturnCodeProvider = startByte => startByte == 545 ? (byte)0x06 : (byte)0xFF;

        byte[] payload = new byte[1024];
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 100, payload)));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcDataTypeNotSupported, exception.ErrorCode);
        Assert.Equal((byte)0x06, exception.PlcReturnCode);
        Assert.Equal(445, exception.BytesWritten);
        Assert.Equal(new WrittenRange(100, 445), exception.WrittenRange);
    }

    [Fact]
    public async Task Write_InputArea_IsRejectedBeforeAnyNetworkTraffic()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();
        int framesBefore = transport.ReceivedFrames.Count;

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => { _ = client.WriteBytesAsync(new S7WriteRequest(S7Area.Input, 0, 0, new byte[] { 1 })); });

        Assert.Equal(S7ErrorCode.AreaNotSupported, exception.ErrorCode);
        Assert.Equal(framesBefore, transport.ReceivedFrames.Count);
    }

    [Fact]
    public async Task Read_WhenNotConnected_ThrowsSessionNotEstablished()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 1)));

        Assert.Equal(S7ErrorCode.SessionNotEstablished, exception.ErrorCode);
        Assert.Empty(transport.ReceivedFrames);
    }

    [Fact]
    public async Task Connect_WhenAlreadyConnected_IsRejectedAndKeepsSession()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ConnectAsync());

        Assert.Equal(S7ErrorCode.SessionAlreadyConnected, exception.ErrorCode);
        Assert.Equal(S7SessionState.Connected, client.State);
        Assert.Equal(1, transport.ConnectedCount);
    }

    [Fact]
    public async Task Disconnect_IsIdempotentAndResetsState()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        await client.DisconnectAsync();
        await client.DisconnectAsync();

        Assert.Equal(S7SessionState.Disconnected, client.State);
        Assert.Equal(0, client.NegotiatedPduSize);
    }

    [Fact]
    public async Task Read_WithFragmentedFrames_Succeeds()
    {
        var transport = new FakeTcpTransport { FragmentSize = 3 };
        FillMemory(transport.Memory, 32);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 16));

        Assert.Equal(transport.Memory.AsSpan(0, 16).ToArray(), data.ToArray());
    }

    [Fact]
    public async Task Connect_CotpRejected_ThrowsConnectionError()
    {
        var transport = new FakeTcpTransport();
        transport.RequestInterceptor = request => request[5] == 0xE0
            ? [0x03, 0x00, 0x00, 0x07, 0x02, 0xF0, 0x80]
            : null;

        await using S7Client client = CreateClient(transport);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ConnectAsync());

        Assert.Equal(S7ErrorCategory.Connection, exception.Category);
        Assert.Equal(S7ErrorCode.CotpConnectionRejected, exception.ErrorCode);
        Assert.Equal(S7SessionState.Disconnected, client.State);
    }

    [Fact]
    public async Task Read_ConnectionClosed_ThrowsAndMarksFaulted()
    {
        var transport = new FakeTcpTransport();
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        await transport.DisposeAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 1)));

        Assert.Equal(S7ErrorCategory.Connection, exception.Category);
        Assert.Equal(S7SessionState.Faulted, client.State);
    }

    [Fact]
    public async Task Read_CancelAfterRequestSent_KeepsSessionUsable()
    {
        var transport = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(300) };
        FillMemory(transport.Memory, 64);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 8), cancellationSource.Token));

        Assert.Equal(S7ErrorCategory.Cancelled, exception.Category);
        Assert.Equal(S7ErrorCode.OperationCancelled, exception.ErrorCode);
        Assert.Equal(S7SessionState.Connected, client.State);

        transport.ReadResponseDelay = TimeSpan.Zero;
        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 8));

        Assert.Equal(8, data.Length);
    }

    [Fact]
    public async Task ConcurrentReads_AreSerializedAndAllSucceed()
    {
        var transport = new FakeTcpTransport();
        FillMemory(transport.Memory, 64);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        Task<ReadOnlyMemory<byte>>[] tasks = Enumerable.Range(0, 16)
            .Select(_ => client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 8)))
            .ToArray();

        ReadOnlyMemory<byte>[] results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(8, result.Length));
    }

    [Fact]
    public async Task Write_Timeout_MarksFaultedAndReportsUncertain()
    {
        var options = CreateOptions() with { WriteTimeout = TimeSpan.FromSeconds(1) };
        var transport = new FakeTcpTransport();
        await using S7Client client = new(options, transport: transport);
        await client.ConnectAsync();

        transport.RequestInterceptor = request => request.Length > 17 && request[17] == 0x05
            ? null
            : transport.DefaultResponseForTest(request);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBytesAsync(new S7WriteRequest(S7Area.Memory, 0, 0, new byte[] { 1 })));

        Assert.Equal(S7ErrorCategory.Timeout, exception.Category);
        Assert.Equal(S7ErrorCode.WriteTimeout, exception.ErrorCode);
        Assert.True(exception.WriteOutcomeUncertain);
        Assert.Equal(S7SessionState.Faulted, client.State);

        S7ClientException followUp = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 1)));
        Assert.Equal(S7ErrorCode.SessionFaulted, followUp.ErrorCode);

        await client.DisconnectAsync();
        Assert.Equal(S7SessionState.Disconnected, client.State);
    }

    [Fact]
    public async Task Connect_SetupWithoutPdu_UsesMinimumChunkSize()
    {
        var transport = new FakeTcpTransport { ConfirmedPduSize = 0 };
        FillMemory(transport.Memory, 512);
        await using S7Client client = CreateClient(transport);
        await client.ConnectAsync();

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 300));

        Assert.Equal(300, data.Length);
        byte[][] readFrames = transport.ReceivedFrames.Where(frame => frame.Length > 17 && frame[17] == 0x04).ToArray();
        Assert.Equal(2, readFrames.Length);
        Assert.Equal(222, GetStartByte(readFrames[1]));
    }

    [Fact]
    public async Task LoggerFailures_DoNotAffectOperations()
    {
        var logger = new TestLogger { ThrowOnUse = true };
        var transport = new FakeTcpTransport();
        FillMemory(transport.Memory, 64);
        await using S7Client client = new(CreateOptions(), logger, transport);

        await client.ConnectAsync();
        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, 8));
        int written = await client.WriteBytesAsync(
            new S7WriteRequest(S7Area.Memory, 0, 0, new byte[] { 1, 2, 3 }));

        Assert.Equal(8, data.Length);
        Assert.Equal(3, written);
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    [Fact]
    public async Task DefaultLogging_DoesNotContainReadWriteData()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        FillMemory(transport.Memory, 64);
        await using S7Client client = new(CreateOptions(), logger, transport);

        await client.ConnectAsync();
        _ = await client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 8));
        _ = await client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 0xDE, 0xAD }));

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.Null(entry.ProtocolTrace));
        Assert.All(logger.Entries, entry => Assert.DoesNotContain("DEAD", entry.Operation, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProtocolTrace_WhenEnabled_RecordsFrames()
    {
        var logger = new TestLogger();
        var transport = new FakeTcpTransport();
        await using S7Client client = new(
            CreateOptions() with { EnableProtocolTrace = true },
            logger,
            transport);

        await client.ConnectAsync();
        _ = await client.ReadBytesAsync(new S7ReadRequest(S7Area.Memory, 0, 0, 1));

        Assert.Contains(logger.Entries, entry => entry.ProtocolTrace is not null);
    }

    [Fact]
    public async Task QueueOverflow_WhenWaitersExceedLimit()
    {
        var options = CreateOptions() with { MaxQueuedRequests = 1, ReadTimeout = TimeSpan.FromSeconds(1) };
        var transport = new FakeTcpTransport();
        await using S7Client client = new(options, transport: transport);
        await client.ConnectAsync();

        transport.RequestInterceptor = request => request.Length > 17 && request[17] == 0x04
            ? null
            : transport.DefaultResponseForTest(request);

        using var cancellationSource = new CancellationTokenSource();
        Task<ReadOnlyMemory<byte>> first = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, 1),
            cancellationSource.Token);

        await WaitUntilAsync(
            () => transport.ReceivedFrames.Any(frame => frame.Length > 17 && frame[17] == 0x04),
            TimeSpan.FromSeconds(2));

        Task<ReadOnlyMemory<byte>> second = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, 1),
            cancellationSource.Token);
        await Task.Delay(50);
        Task<ReadOnlyMemory<byte>> third = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.Memory, 0, 0, 1),
            cancellationSource.Token);

        S7ClientException overflow = await Assert.ThrowsAsync<S7ClientException>(() => third);
        Assert.Equal(S7ErrorCode.QueueOverflow, overflow.ErrorCode);

        cancellationSource.Cancel();

        S7ClientException firstError = await Assert.ThrowsAsync<S7ClientException>(() => first);
        Assert.Equal(S7ErrorCode.OperationCancelled, firstError.ErrorCode);

        S7ClientException secondError = await Assert.ThrowsAsync<S7ClientException>(() => second);
        Assert.Equal(S7ErrorCode.OperationCancelled, secondError.ErrorCode);
    }

    [Fact]
    public async Task Read_TimeoutAfterRequestSent_KeepsSessionUsable()
    {
        var options = CreateOptions() with { ReadTimeout = TimeSpan.FromSeconds(1) };
        var transport = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(1200) };
        FillMemory(transport.Memory, 64);
        await using S7Client client = new(options, transport: transport);
        await client.ConnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 8)));

        Assert.Equal(S7ErrorCategory.Timeout, exception.Category);
        Assert.Equal(S7ErrorCode.ReadTimeout, exception.ErrorCode);
        Assert.Equal(S7SessionState.Connected, client.State);

        transport.ReadResponseDelay = TimeSpan.Zero;
        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 8));

        Assert.Equal(8, data.Length);
    }

    private static S7ConnectionOptions CreateOptions()
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            RequestedPduSize = 480
        };
    }

    private static S7Client CreateClient(FakeTcpTransport transport)
    {
        return new S7Client(CreateOptions(), transport: transport);
    }

    private static void FillMemory(byte[] memory, int length)
    {
        for (int i = 0; i < length; i++)
        {
            memory[i] = (byte)((i * 7 + 11) & 0xFF);
        }
    }

    private static int GetStartByte(byte[] frame)
    {
        return ((frame[28] << 16) | (frame[29] << 8) | frame[30]) / 8;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        long start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeout.TotalMilliseconds)
            {
                throw new TimeoutException("条件未在超时内满足。");
            }

            await Task.Delay(10);
        }
    }
}