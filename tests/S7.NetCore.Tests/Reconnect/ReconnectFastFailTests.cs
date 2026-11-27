using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using S7.NetCore;
using S7.NetCore.Internal;
using S7.NetCore.Tests;

namespace S7.NetCore.Tests.Reconnect;

public sealed class ReconnectFastFailTests
{
    [Fact]
    public async Task FaultCall_ReturnsWithoutWaitingSubscriber()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(CreateOptions(), null, fake, () => fake);
        client.SessionStateChanged += static (_, _) => Thread.Sleep(400);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        long start = Stopwatch.GetTimestamp();
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(150), $"断线返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task FaultCall_ReturnsWithoutWaitingLogger()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(CreateOptions(), logger, fake, () => fake);
        await client.ConnectAsync();
        logger.LogDelay = TimeSpan.FromMilliseconds(400);
        fake.FailAfterRequestCount = 3;

        long start = Stopwatch.GetTimestamp();
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(150), $"断线返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task FaultCall_DoesNotAwaitTransportClose()
    {
        var fake = new FakeTcpTransport
        {
            DisposeDelay = TimeSpan.FromMilliseconds(400)
        };
        await using S7Client client = new S7Client(CreateOptions(), null, fake, () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        long start = Stopwatch.GetTimestamp();
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(150), $"断线返回耗时 {elapsed.TotalMilliseconds} ms。");
    }

    [Fact]
    public async Task ConcurrentFault_SingleNotification_SingleTransition()
    {
        var fake = new FakeTcpTransport();
        var notifications = new ConcurrentQueue<(S7SessionState Previous, S7SessionState Current)>();
        await using S7Session session = CreateSession(fake);
        session.StateChanged += (_, e) => notifications.Enqueue((e.PreviousState, e.CurrentState));
        await session.ConnectAsync(CancellationToken.None);

        bool TryFault()
        {
            return session.TryBeginFaultTransition(
                fake,
                S7SessionState.Reconnecting,
                out _,
                out _);
        }

        bool[] results = await Task.WhenAll(Task.Run(TryFault), Task.Run(TryFault));

        Assert.Equal(1, results.Count(static value => value));
        await WaitUntilAsync(() => notifications.Any(static pair =>
            pair.Previous == S7SessionState.Connected && pair.Current == S7SessionState.Reconnecting));
        await Task.Delay(50);
        Assert.Equal(1, notifications.Count(static pair =>
            pair.Previous == S7SessionState.Connected && pair.Current == S7SessionState.Reconnecting));
    }

    [Fact]
    public async Task QueuedRequest_TerminatedWith204_WhenReconnectStarts()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);

        fake.WriteResponseDelay = TimeSpan.FromMilliseconds(200);
        fake.FailConnectAttempts = 5;

        Task<int> running = session.WriteAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1 }),
            CancellationToken.None);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        Task<int> queued = session.WriteAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 1, new byte[] { 2 }),
            CancellationToken.None);
        await Task.Delay(50);

        long start = Stopwatch.GetTimestamp();
        await fake.DisposeAsync();
        S7ClientException queuedError = await Assert.ThrowsAsync<S7ClientException>(() => queued);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.SessionReconnecting, queuedError.ErrorCode);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), $"排队终结耗时 {elapsed.TotalMilliseconds} ms。");

        S7ClientException runningError = await Assert.ThrowsAsync<S7ClientException>(() => running);
        Assert.Equal(S7ErrorCode.ConnectionReset, runningError.ErrorCode);
    }

    [Fact]
    public async Task ChunkedRead_MidwayReconnect_Returns204_WithoutPartialData()
    {
        var fake = new FakeTcpTransport
        {
            ConfirmedPduSize = 240
        };
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);
        fake.ReadResponseDelay = TimeSpan.FromMilliseconds(200);

        Task<ReadOnlyMemory<byte>> read = session.ReadAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 400),
            CancellationToken.None);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));
        Assert.Same(fake, legacy);

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => read);
        Assert.Equal(S7ErrorCode.SessionReconnecting, error.ErrorCode);
        Assert.Equal(0, error.BytesWritten);
        Assert.Null(error.WrittenRange);
        Assert.Equal(3, fake.WriteCount);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task ChunkedWrite_MidwayReconnect_Returns204_WithProgress()
    {
        var fake = new FakeTcpTransport
        {
            ConfirmedPduSize = 240
        };
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);
        fake.WriteResponseDelay = TimeSpan.FromMilliseconds(200);

        Task<int> write = session.WriteAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[400]),
            CancellationToken.None);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));
        Assert.Same(fake, legacy);

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => write);
        Assert.Equal(S7ErrorCode.SessionReconnecting, error.ErrorCode);
        Assert.True(error.BytesWritten > 0);
        Assert.NotNull(error.WrittenRange);
        Assert.Equal(0, error.WrittenRange!.Value.StartByte);
        Assert.Equal(error.BytesWritten, error.WrittenRange.Value.Length);
        Assert.False(error.WriteOutcomeUncertain);
        Assert.Equal(3, fake.WriteCount);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task BatchFrame_NotSent_WhenReconnectBeforeSend()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);
        int framesBefore = fake.WriteCount;

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));

        var readItems = new[]
        {
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[4])
        };
        S7ClientException readError = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.ReadBatchAsync(readItems, CancellationToken.None));

        var writeItems = new[]
        {
            new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[1])
        };
        S7ClientException writeError = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.WriteBatchAsync(writeItems, CancellationToken.None));

        Assert.Equal(S7ErrorCode.SessionReconnecting, readError.ErrorCode);
        Assert.Equal(S7ErrorCode.SessionReconnecting, writeError.ErrorCode);
        Assert.Equal(framesBefore, fake.WriteCount);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReconnectingRequest_SendsNoFrame()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);
        int framesBefore = fake.WriteCount;

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));

        S7ClientException read = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.ReadAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4), CancellationToken.None));
        S7ClientException write = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.WriteAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1 }), CancellationToken.None));
        S7ClientException bitWrite = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.WriteBitAsync(
                new S7WriteRequest(S7Area.DataBlock, 1, 0, ReadOnlyMemory<byte>.Empty),
                0,
                true,
                CancellationToken.None));

        Assert.Equal(S7ErrorCode.SessionReconnecting, read.ErrorCode);
        Assert.Equal(S7ErrorCode.SessionReconnecting, write.ErrorCode);
        Assert.Equal(S7ErrorCode.SessionReconnecting, bitWrite.ErrorCode);
        Assert.Equal(framesBefore, fake.WriteCount);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancelledRequestDuringReconnect_Returns603()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);

        Assert.True(session.TryBeginFaultTransition(
            fake,
            S7SessionState.Reconnecting,
            out ITcpTransport? legacy,
            out _));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            session.ReadAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 4),
                cancellation.Token));

        Assert.Equal(S7ErrorCode.OperationCancelled, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Cancelled, error.Category);

        if (legacy is not null)
        {
            await legacy.DisposeAsync();
        }
    }

    [Fact]
    public async Task QueuedRequest_Cancelled_Returns603()
    {
        var fake = new FakeTcpTransport();
        await using S7Session session = CreateSession(fake);
        await session.ConnectAsync(CancellationToken.None);
        fake.WriteResponseDelay = TimeSpan.FromMilliseconds(200);

        Task<int> running = session.WriteAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1 }),
            CancellationToken.None);
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        using var cancellation = new CancellationTokenSource();
        Task<int> queued = session.WriteAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 1, new byte[] { 2 }),
            cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() => queued);
        Assert.Equal(S7ErrorCode.OperationCancelled, error.ErrorCode);
        Assert.Equal(1, await running);
    }

    [Fact]
    public async Task NotificationOrder_StrictlyFollowsTransitions()
    {
        var fake = new FakeTcpTransport();
        var transitions = new ConcurrentQueue<(S7SessionState Previous, S7SessionState Current)>();
        await using S7Client client = new S7Client(CreateOptions(), null, fake, () => fake);
        client.SessionStateChanged += (_, e) => transitions.Enqueue((e.PreviousState, e.CurrentState));

        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;
        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        await WaitUntilAsync(() => client.State == S7SessionState.Connected && fake.ConnectedCount >= 2);
        await WaitUntilAsync(() => transitions.Any(static pair =>
            pair.Previous == S7SessionState.Reconnecting && pair.Current == S7SessionState.Connected));

        (S7SessionState Previous, S7SessionState Current)[] items = transitions.ToArray();
        int reconnectStart = Array.FindIndex(items, static pair =>
            pair.Previous == S7SessionState.Connected && pair.Current == S7SessionState.Reconnecting);
        int reconnectEnd = Array.FindIndex(items, static pair =>
            pair.Previous == S7SessionState.Reconnecting && pair.Current == S7SessionState.Connected);

        Assert.True(reconnectStart >= 0);
        Assert.True(reconnectEnd > reconnectStart);
    }

    [Fact]
    public async Task Dispose_AbortsQueuedRequest_WithinOneSecond()
    {
        var fake = new FakeTcpTransport();
        S7Client client = new S7Client(CreateOptions(), null, fake, () => fake);
        await client.ConnectAsync();
        fake.WriteResponseDelay = TimeSpan.FromMilliseconds(200);

        Task<int> running = client.WriteBytesAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1 }));
        await WaitUntilAsync(() => fake.WriteCount >= 3);

        Task<int> queued = client.WriteBytesAsync(
            new S7WriteRequest(S7Area.DataBlock, 1, 1, new byte[] { 2 }));
        await Task.Delay(50);

        long start = Stopwatch.GetTimestamp();
        await client.DisposeAsync();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"释放耗时 {elapsed.TotalMilliseconds} ms。");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        Assert.Equal(1, await running);
    }

    [Fact]
    public async Task Timeout_DrainFails_Returns202_Not204()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(readTimeout: TimeSpan.FromSeconds(1)),
            null,
            fake,
            () => fake);
        await client.ConnectAsync();
        fake.RequestInterceptor = request => request.Length > 17 && request[17] == 0x04
            ? null
            : fake.DefaultResponseForTest(request);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.SessionFaulted, exception.ErrorCode);
        Assert.NotEqual(S7ErrorCode.SessionReconnecting, exception.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);
    }

    [Fact]
    public void PublicApiSurface_Unchanged()
    {
        Assembly assembly = typeof(S7Client).Assembly;

        Type? pump = assembly.GetType("S7.NetCore.Internal.SideEffectPump");
        Type? decision = assembly.GetType("S7.NetCore.Internal.ReconnectDecisionInfo");

        Assert.NotNull(pump);
        Assert.NotNull(decision);
        Assert.False(pump!.IsPublic);
        Assert.False(decision!.IsPublic);
        Assert.Null(typeof(S7ConnectionOptions).GetProperty("SideEffectPump"));
        Assert.Null(typeof(S7ConnectionOptions).GetProperty("ConnectionLostCts"));
        Assert.Null(typeof(S7LogEntry).GetProperty("ReconnectDecision"));
    }

    private static S7Session CreateSession(FakeTcpTransport fake, IS7Logger? logger = null)
    {
        return new S7Session(
            CreateOptions(),
            logger,
            fake,
            TimeProvider.System,
            () => fake);
    }

    private static S7ConnectionOptions CreateOptions(
        bool autoReconnectEnabled = true,
        TimeSpan? readTimeout = null)
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(2),
            WriteTimeout = TimeSpan.FromSeconds(2),
            RequestedPduSize = 480,
            AutoReconnectEnabled = autoReconnectEnabled
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