using System.Diagnostics;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Reconnect;

public sealed class ReconnectFastFailSingleTests
{
    [Fact]
    public async Task ReadTimeout_DrainSuccess_Returns601()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(1200) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ReadTimeout, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Timeout, error.Category);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2000), $"超时返回耗时 {elapsed.TotalMilliseconds} ms。");
        Assert.Equal(S7SessionState.Connected, client.State);
        Assert.Equal(1, fake.ConnectedCount);
    }

    [Fact]
    public async Task ReadTimeout_NextRead_SameConnection()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(1300) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        S7ClientException timeout = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        Assert.Equal(S7ErrorCode.ReadTimeout, timeout.ErrorCode);
        Assert.Equal(S7SessionState.Connected, client.State);

        fake.ReadResponseDelay = TimeSpan.Zero;
        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));

        Assert.Equal(4, data.Length);
        Assert.Equal(S7SessionState.Connected, client.State);
        Assert.Equal(1, fake.ConnectedCount);
    }

    [Fact]
    public async Task ReadTimeout_DrainFails_Returns202AndFaulted()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(10) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2600), $"排空失败返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task CancelAfterSend_DrainSuccess_Returns603()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(1) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(10)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        using var cancellation = new CancellationTokenSource();
        Task<ReadOnlyMemory<byte>> read = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4),
            cancellation.Token);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        long start = Stopwatch.GetTimestamp();
        cancellation.Cancel();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => read);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.OperationCancelled, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Cancelled, error.Category);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2500), $"取消返回耗时 {elapsed.TotalMilliseconds} ms。");
        Assert.Equal(S7SessionState.Connected, client.State);
    }

    [Fact]
    public async Task Cancel_DrainFails_Returns202Not603()
    {
        var fake = new FakeTcpTransport();
        fake.RequestInterceptor = request => request.Length > 17 && request[17] == 0x04
            ? null
            : fake.DefaultResponseForTest(request);
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(10)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        using var cancellation = new CancellationTokenSource();
        Task<ReadOnlyMemory<byte>> read = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4),
            cancellation.Token);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        long start = Stopwatch.GetTimestamp();
        cancellation.Cancel();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => read);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2600), $"取消排空失败耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task WriteTimeout_DrainFails_202KeepsWriteProgress()
    {
        var fake = new FakeTcpTransport();
        int writeRequests = 0;
        fake.RequestInterceptor = request =>
        {
            if (request.Length > 17 && request[17] == 0x05)
            {
                writeRequests++;
                if (writeRequests >= 2)
                {
                    return null;
                }
            }

            return fake.DefaultResponseForTest(request);
        };

        await using S7Client client = new S7Client(
            CreateOptions(writeTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        var payload = new byte[1000];
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, payload)));

        Assert.Equal(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.True(error.WriteOutcomeUncertain);
        Assert.True(error.BytesWritten > 0, $"已写字节数 {error.BytesWritten}。");
        Assert.True(error.BytesWritten < payload.Length, $"已写字节数 {error.BytesWritten}。");
        Assert.NotNull(error.WrittenRange);
        Assert.Equal(0, error.WrittenRange!.Value.StartByte);
        Assert.Equal(error.BytesWritten, error.WrittenRange.Value.Length);
        Assert.Equal(S7SessionState.Faulted, client.State);
    }

    [Fact]
    public async Task Drain_DuringDisconnect_ReturnsOriginalError()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(10) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        Task<ReadOnlyMemory<byte>> read = client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));
        await Task.Delay(1100);

        long start = Stopwatch.GetTimestamp();
        await client.DisconnectAsync(CancellationToken.None);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => read);
        Assert.Equal(S7ErrorCode.ReadTimeout, error.ErrorCode);
        Assert.NotEqual(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.Equal(S7SessionState.Disconnected, client.State);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"断开释放耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task Drain_RejectsMismatchedResponseType()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(1200) };
        fake.RequestInterceptor = request =>
        {
            if (request.Length > 17 && request[17] == 0x04)
            {
                byte[]? response = fake.DefaultResponseForTest(request);
                if (response is not null)
                {
                    response[19] = 0x05;
                }

                return response;
            }

            return fake.DefaultResponseForTest(request);
        };

        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2600), $"排空失败返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task FailureDrain_NoReconnectStarted()
    {
        var fake = new FakeTcpTransport();
        fake.RequestInterceptor = request => request.Length > 17 && request[17] == 0x04
            ? null
            : fake.DefaultResponseForTest(request);
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.SessionFaulted, error.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);

        S7ClientException again = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.SessionFaulted, again.ErrorCode);
        Assert.Equal(1, fake.ConnectedCount);
    }

    [Fact]
    public async Task SendPath_HasNoPreDrain()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromMilliseconds(1200) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        int readCountBefore = fake.ReadCount;
        int readsAtSecondWrite = -1;
        fake.RequestInterceptor = request =>
        {
            if (request.Length > 17 && request[17] == 0x04)
            {
                readsAtSecondWrite = fake.ReadCount;
            }

            return fake.DefaultResponseForTest(request);
        };
        fake.ReadResponseDelay = TimeSpan.Zero;

        _ = await client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));

        Assert.Equal(readCountBefore, readsAtSecondWrite);
    }

    [Fact]
    public async Task Fault_LoggerIsEnabledBlocks_ReturnsUnder5ms()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(CreateOptions(), logger, fake, () => fake);
        await client.ConnectAsync();

        logger.IsEnabledDelay = TimeSpan.FromMilliseconds(500);
        fake.FailAfterRequestCount = 3;

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ConnectionReset, error.ErrorCode);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(150), $"失败返回耗时 {elapsed.TotalMilliseconds} ms。");
        await WaitUntilAsync(() => logger.SnapshotEntries().Any(static entry => !entry.Succeeded));
    }

    [Fact]
    public async Task Fault_LoggerThrows_ResultUnaffected()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(CreateOptions(), logger, fake, () => fake);
        await client.ConnectAsync();

        logger.ThrowOnUse = true;
        fake.FailAfterRequestCount = 3;

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        Assert.Equal(S7ErrorCode.ConnectionReset, error.ErrorCode);

        await WaitUntilAsync(() => client.State == S7SessionState.Connected && fake.ConnectedCount >= 2);
        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));
        Assert.Equal(4, data.Length);
    }

    [Fact]
    public async Task BatchWriteTimeout_DrainSuccess_Returns602()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(
            fake,
            CreateOptions(writeTimeout: TimeSpan.FromSeconds(1)));
        await session.ConnectAsync(CancellationToken.None);
        fake.WriteResponseDelay = TimeSpan.FromMilliseconds(1300);

        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[] { 1 })
        ];

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.WriteBatchAsync(items, CancellationToken.None));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.WriteTimeout, error.ErrorCode);
        Assert.True(error.WriteOutcomeUncertain);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(2000), $"批量写超时返回耗时 {elapsed.TotalMilliseconds} ms。");
        Assert.Equal(S7SessionState.Connected, session.State);
    }

    [Fact]
    public async Task BatchReadFault_Returns301_NoItems_Under5ms()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake, CreateOptions());
        await session.ConnectAsync(CancellationToken.None);
        fake.FailAfterRequestCount = 3;

        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[4]),
            new BatchWireItem(S7Area.DataBlock, 1, 4, new byte[4])
        ];

        long start = Stopwatch.GetTimestamp();
        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.ReadBatchAsync(items, CancellationToken.None));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ConnectionReset, error.ErrorCode);
        Assert.Empty(error.BatchFailedItems);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(150), $"批量读断线返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task Reconnecting_204_DoesNotCloseReconnectTransport()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake, CreateOptions());
        await session.ConnectAsync(CancellationToken.None);

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));
        Assert.True(fake.IsOpen);

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.ReadAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4), CancellationToken.None));

        Assert.Equal(S7ErrorCode.SessionReconnecting, error.ErrorCode);
        Assert.True(fake.IsOpen);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailureLog_ContainsReasonAndElapsed()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(CreateOptions(), logger, fake, () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        await WaitUntilAsync(() => logger.SnapshotEntries().Any(static entry => !entry.Succeeded));

        Exception? cause = logger.SnapshotCauses().FirstOrDefault(static item => item is ReconnectDecisionInfo);
        ReconnectDecisionInfo? decision = Assert.IsType<ReconnectDecisionInfo>(cause);
        Assert.Contains("未重试、未退避", decision.Reason);
        Assert.DoesNotContain("延后", decision.Reason);
        Assert.Contains("判定耗时", decision.Message);
    }

    [Fact]
    public void PublicApiSurface_NoRecoveryMetadata()
    {
        Assert.Null(typeof(S7LogEntry).GetProperty("Reason"));
        Assert.Null(typeof(S7LogEntry).GetProperty("JudgmentElapsed"));
        Assert.Null(typeof(S7ClientException).GetProperty("Retried"));
        Assert.Null(typeof(S7ClientException).GetProperty("RecoveryCount"));
        Assert.Null(typeof(S7ConnectionOptions).GetProperty("FastFailEnabled"));
    }

    private static S7Session CreateSession(FakeTcpTransport fake, S7ConnectionOptions options, IS7Logger? logger = null)
    {
        return new S7Session(options, logger, fake, TimeProvider.System, () => fake);
    }

    private static S7ConnectionOptions CreateOptions(
        TimeSpan? readTimeout = null,
        TimeSpan? writeTimeout = null)
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(2),
            WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds(2),
            RequestedPduSize = 480,
            AutoReconnectEnabled = true
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(10);
        }
    }
}