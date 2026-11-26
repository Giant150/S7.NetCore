using System.Diagnostics;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Reconnect;

public sealed class ReconnectFastFailSingleTests
{
    [Fact]
    public void AftercareQueue_PostTakeClear()
    {
        var queue = new AftercareQueue();
        var fake = new FakeTcpTransport();
        var pending = new PendingLateResponse(
            fake,
            pduReference: 7,
            S7OperationStage.Read,
            TimeSpan.FromSeconds(1),
            connectionEpoch: 3);

        queue.Post(in pending);
        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryTake(out PendingLateResponse taken));
        Assert.Same(fake, taken.Transport);
        Assert.Equal((ushort)7, taken.PduReference);
        Assert.Equal(S7OperationStage.Read, taken.Stage);
        Assert.Equal(TimeSpan.FromSeconds(1), taken.DrainTimeout);
        Assert.Equal(3, taken.ConnectionEpoch);
        Assert.False(queue.TryTake(out _));

        queue.Post(in pending);
        queue.Clear();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ReadTimeout_Returns601_NoDrainWait()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(3) };
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
        Assert.True(elapsed < TimeSpan.FromMilliseconds(1600), $"超时返回耗时 {elapsed.TotalMilliseconds} ms。");
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
    public async Task Timeout_DeferredDrain_ThenFaulted202()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(10) };
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();

        S7ClientException timeout = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        Assert.Equal(S7ErrorCode.ReadTimeout, timeout.ErrorCode);

        S7ClientException faulted = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.SessionFaulted, faulted.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);
    }

    [Fact]
    public async Task CancelAfterSend_Returns603_NoDrainWait()
    {
        var fake = new FakeTcpTransport { ReadResponseDelay = TimeSpan.FromSeconds(3) };
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
        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), $"取消返回耗时 {elapsed.TotalMilliseconds} ms。");
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
    public async Task BatchWriteTimeout_Returns602_NoDrainWait()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(
            fake,
            CreateOptions(writeTimeout: TimeSpan.FromSeconds(1)));
        await session.ConnectAsync(CancellationToken.None);
        fake.WriteResponseDelay = TimeSpan.FromSeconds(3);

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
        Assert.True(elapsed < TimeSpan.FromMilliseconds(1600), $"批量写超时返回耗时 {elapsed.TotalMilliseconds} ms。");
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
        Assert.Contains("善后已延后", decision.Reason);
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
