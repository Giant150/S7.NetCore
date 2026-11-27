using System.Diagnostics;
using System.Reflection;
using S7.NetCore;
using S7.NetCore.Internal;
using S7.NetCore.Tests;

namespace S7.NetCore.Tests.Reconnect;

public sealed class ReconnectTests
{
    [Fact]
    public void DefaultOptions_EnableReconnect_AndExposeSingleSwitch()
    {
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200
        };

        Assert.True(options.AutoReconnectEnabled);

        PropertyInfo[] reconnectMembers = typeof(S7ConnectionOptions)
            .GetProperties()
            .Where(static property => property.Name.Contains("Reconnect", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(reconnectMembers);
        Assert.Equal(nameof(S7ConnectionOptions.AutoReconnectEnabled), reconnectMembers[0].Name);

        Assert.Null(typeof(S7Client).Assembly.GetType("S7.NetCore.S7ReconnectPolicy"));
        Assert.Null(typeof(S7Client).GetEvent("ReconnectEvent"));
        Assert.Null(typeof(S7LogEntry).GetProperty("MaxAttempts"));
    }

    [Fact]
    public void ResultTypes_DoNotExposeReconnectMetadata()
    {
        Type[] types =
        [
            typeof(S7TypedReadResult),
            typeof(S7StructReadResult<>),
            typeof(S7BatchReadResult),
            typeof(S7BatchWriteResult),
            typeof(S7TypedBatchReadResult),
            typeof(S7TypedBatchWriteResult)
        ];
        string[] members = ["RecoveryCount", "Retried", "ReplayAttempted", "ReplayedBytes"];

        foreach (Type type in types)
        {
            foreach (string member in members)
            {
                Assert.Null(type.GetProperty(member));
            }
        }
    }

    [Fact]
    public void PlaceholderErrorCodes_AreNotObsolete()
    {
        Assert.Null(GetObsolete(nameof(S7ErrorCode.InvalidReconnectPolicy)));
        Assert.Null(GetObsolete(nameof(S7ErrorCode.ReconnectExhausted)));
        Assert.Null(GetObsolete(nameof(S7ErrorCode.ReconnectAborted)));
        Assert.Equal(106, (int)S7ErrorCode.InvalidReconnectPolicy);
        Assert.Equal(604, (int)S7ErrorCode.ReconnectExhausted);
        Assert.Equal(605, (int)S7ErrorCode.ReconnectAborted);
    }

    [Fact]
    public void BackoffSequence_IsFixedWithoutJitter()
    {
        Assert.Equal(TimeSpan.Zero, BackoffDelayCalculator.Calculate(1));
        Assert.Equal(TimeSpan.FromSeconds(1), BackoffDelayCalculator.Calculate(2));
        Assert.Equal(TimeSpan.FromSeconds(2), BackoffDelayCalculator.Calculate(3));
        Assert.Equal(TimeSpan.FromSeconds(4), BackoffDelayCalculator.Calculate(4));
        Assert.Equal(TimeSpan.FromSeconds(8), BackoffDelayCalculator.Calculate(5));
        Assert.Equal(TimeSpan.FromSeconds(16), BackoffDelayCalculator.Calculate(6));
        Assert.Equal(TimeSpan.FromSeconds(30), BackoffDelayCalculator.Calculate(7));
        Assert.Equal(TimeSpan.FromSeconds(30), BackoffDelayCalculator.Calculate(8));
    }

    [Fact]
    public async Task ConnectionLoss_ReturnsOriginalError_ThenNextReadSucceeds()
    {
        var fake = new FakeTcpTransport();
        fake.Memory[0] = 0x12;
        fake.Memory[1] = 0x34;
        fake.Memory[2] = 0x56;
        fake.Memory[3] = 0x78;
        var logger = new TestLogger();
        var states = new List<S7SessionState>();

        await using S7Client client = new S7Client(
            CreateOptions(),
            logger,
            fake,
            transportFactory: () => fake);
        client.SessionStateChanged += (_, e) => states.Add(e.CurrentState);

        await client.ConnectAsync();
        SessionSnapshot before = client.Snapshot;
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);

        await WaitUntilAsync(() => client.State == S7SessionState.Connected
            && fake.ConnectedCount >= 2
            && states.Contains(S7SessionState.Reconnecting)
            && states.Count > 0
            && states[^1] == S7SessionState.Connected);

        ReadOnlyMemory<byte> data = await client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, data.ToArray());
        Assert.Contains(S7SessionState.Reconnecting, states);
        Assert.Equal(S7SessionState.Connected, states[^1]);

        SessionSnapshot after = client.Snapshot;
        Assert.NotEqual(before.SessionId, after.SessionId);
        Assert.True(after.NegotiatedPduSize > 0);
    }

    [Fact]
    public async Task FirstReconnectAttempt_HasNoBackoff()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(
            CreateOptions(),
            logger,
            fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Connected && fake.ConnectedCount >= 2);

        S7LogEntry firstAttempt = logger.Records
            .Where(record => record.Entry.Operation == "Reconnect" && record.Entry.Attempt == 1)
            .Select(record => record.Entry)
            .First();
        Assert.Equal(TimeSpan.Zero, firstAttempt.ReconnectDelay);
    }

    [Fact]
    public async Task ExplicitFalse_KeepsBaselineFaultedBehavior()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(autoReconnectEnabled: false),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);

        S7ClientException followUp = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        Assert.Equal(S7ErrorCode.SessionFaulted, followUp.ErrorCode);
    }

    [Fact]
    public async Task ReadDuringReconnect_FailsFastWith204()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        long start = Stopwatch.GetTimestamp();
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(S7ErrorCode.SessionReconnecting, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
        Assert.True(elapsed < TimeSpan.FromMilliseconds(500));

        await client.DisconnectAsync();
        Assert.Equal(S7SessionState.Disconnected, client.State);
    }

    [Fact]
    public async Task CancelledRequestDuringReconnect_Returns603()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4), cancellation.Token));

        Assert.Equal(S7ErrorCode.OperationCancelled, exception.ErrorCode);
        Assert.Equal(S7ErrorCategory.Cancelled, exception.Category);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task WriteDuringReconnect_Returns204_WithoutReplay()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        int writeFramesBefore = CountWriteFrames(fake);
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1, 2, 3, 4 })));

        Assert.Equal(S7ErrorCode.SessionReconnecting, exception.ErrorCode);
        Assert.Equal(writeFramesBefore, CountWriteFrames(fake));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task UncertainWrite_IsNeverReplayed()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1, 2, 3, 4 })));

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.True(exception.WriteOutcomeUncertain);
        Assert.Equal(1, CountWriteFrames(fake));

        await WaitUntilAsync(() => client.State == S7SessionState.Connected && fake.ConnectedCount >= 2);
        Assert.Equal(1, CountWriteFrames(fake));
    }

    [Fact]
    public async Task Snapshot_ReportsReconnectingState_WithFourFields()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        SessionSnapshot snapshot = client.Snapshot;
        Assert.Equal(S7SessionState.Reconnecting, snapshot.State);
        Assert.True(snapshot.AutoReconnectEnabled);
        Assert.Equal(Guid.Empty, snapshot.SessionId);
        Assert.Equal(0, snapshot.NegotiatedPduSize);
        Assert.Null(typeof(SessionSnapshot).GetProperty("ReconnectAttempt"));
        Assert.Null(typeof(SessionSnapshot).GetProperty("LastReconnectAt"));
        Assert.Null(typeof(SessionSnapshot).GetProperty("NextReconnectDelay"));

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task DisconnectWhileReconnecting_StopsWithinOneSecond()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(
            CreateOptions(),
            logger,
            fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        long start = Stopwatch.GetTimestamp();
        await client.DisconnectAsync();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.True(elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(S7SessionState.Disconnected, client.State);
        Assert.DoesNotContain(logger.Records, static record => record.Entry.ErrorCode == S7ErrorCode.ReconnectAborted);
        Assert.Contains(
            logger.Records,
            static record => record.Level == S7LogLevel.Warning && record.Entry.Operation == "Reconnect");
    }

    [Fact]
    public async Task ConnectWhileReconnecting_Returns201()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 5;
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() => client.ConnectAsync());
        Assert.Equal(S7ErrorCode.SessionAlreadyConnected, exception.ErrorCode);
        Assert.Equal(1, fake.ConnectedCount);

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task ReconnectLog_ContainsAttemptAndDelay_WithoutMaxAttempts()
    {
        var fake = new FakeTcpTransport();
        var logger = new TestLogger();
        await using S7Client client = new S7Client(
            CreateOptions(),
            logger,
            fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));
        await WaitUntilAsync(() => client.State == S7SessionState.Connected && fake.ConnectedCount >= 2);

        S7LogEntry[] reconnectEntries = logger.Records
            .Where(static record => record.Entry.Operation == "Reconnect")
            .Select(static record => record.Entry)
            .ToArray();

        Assert.NotEmpty(reconnectEntries);
        Assert.All(reconnectEntries, static entry => Assert.NotNull(entry.Attempt));
        Assert.All(reconnectEntries, static entry => Assert.NotNull(entry.ReconnectDelay));
        Assert.Null(typeof(S7LogEntry).GetProperty("MaxAttempts"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteTimeout_DrainFails_Returns202IndependentOfSwitch(bool autoReconnectEnabled)
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(autoReconnectEnabled, writeTimeout: TimeSpan.FromSeconds(1)),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.RequestInterceptor = request => request.Length > 17 && request[17] == 0x05
            ? null
            : fake.DefaultResponseForTest(request);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteBytesAsync(new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1 })));

        Assert.Equal(S7ErrorCode.SessionFaulted, exception.ErrorCode);
        Assert.True(exception.WriteOutcomeUncertain);
        Assert.Equal(S7SessionState.Faulted, client.State);
        Assert.Equal(1, fake.ConnectedCount);
    }

    private static ObsoleteAttribute? GetObsolete(string fieldName)
    {
        FieldInfo? field = typeof(S7ErrorCode).GetField(fieldName);
        Assert.NotNull(field);
        return field.GetCustomAttribute<ObsoleteAttribute>();
    }

    private static int CountWriteFrames(FakeTcpTransport fake)
    {
        return fake.ReceivedFrames.Count(static frame => frame.Length > 17 && frame[17] == 0x05);
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

    private static S7ConnectionOptions CreateOptions(
        bool autoReconnectEnabled = true,
        TimeSpan? writeTimeout = null)
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds(2),
            RequestedPduSize = 480,
            AutoReconnectEnabled = autoReconnectEnabled
        };
    }
}