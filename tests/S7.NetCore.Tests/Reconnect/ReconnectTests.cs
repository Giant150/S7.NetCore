using S7.NetCore;
using S7.NetCore.Internal;
using S7.NetCore.Tests;

namespace S7.NetCore.Tests.Reconnect;

public sealed class ReconnectTests
{
    [Fact]
    public void PolicyDefaultsAndEnumValues_AreStable()
    {
        S7ReconnectPolicy policy = S7ReconnectPolicy.Disabled;
        Assert.False(policy.AutoReconnectEnabled);
        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
        Assert.Equal(2.0, policy.BackoffMultiplier);
        Assert.Equal(0.2, policy.JitterFactor);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.WaitTimeout);
        Assert.False(policy.ReconnectOnTimeout);
        Assert.False(policy.ReplayUncertainWrites);

        Assert.Equal(4, (int)S7SessionState.Reconnecting);
        Assert.Equal(7, (int)S7OperationStage.Reconnect);
        Assert.Equal(106, (int)S7ErrorCode.InvalidReconnectPolicy);
        Assert.Equal(204, (int)S7ErrorCode.SessionReconnecting);
        Assert.Equal(604, (int)S7ErrorCode.ReconnectExhausted);
        Assert.Equal(605, (int)S7ErrorCode.ReconnectAborted);

        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200
        };
        Assert.Same(S7ReconnectPolicy.Disabled, options.Reconnect);
    }

    [Fact]
    public void InvalidPolicies_AreRejectedBeforeAnyConnect()
    {
        AssertPolicyRejected(new S7ReconnectPolicy { MaxAttempts = -1 }, S7ErrorCode.InvalidReconnectPolicy);
        AssertPolicyRejected(new S7ReconnectPolicy { MaxAttempts = 101 }, S7ErrorCode.InvalidReconnectPolicy);
        AssertPolicyRejected(
            new S7ReconnectPolicy { InitialDelay = TimeSpan.FromMilliseconds(50) },
            S7ErrorCode.InvalidTimeout);
        AssertPolicyRejected(
            new S7ReconnectPolicy { MaxDelay = TimeSpan.FromMilliseconds(500) },
            S7ErrorCode.InvalidTimeout);
        AssertPolicyRejected(
            new S7ReconnectPolicy { InitialDelay = TimeSpan.FromSeconds(10), MaxDelay = TimeSpan.FromSeconds(1) },
            S7ErrorCode.InvalidReconnectPolicy);
        AssertPolicyRejected(
            new S7ReconnectPolicy { WaitTimeout = TimeSpan.FromSeconds(121) },
            S7ErrorCode.InvalidTimeout);
        AssertPolicyRejected(
            new S7ReconnectPolicy { BackoffMultiplier = 0.9 },
            S7ErrorCode.InvalidReconnectPolicy);
        AssertPolicyRejected(
            new S7ReconnectPolicy { JitterFactor = 0.51 },
            S7ErrorCode.InvalidReconnectPolicy);
    }

    [Fact]
    public void Backoff_MatchesExpectedSequenceAndJitterBounds()
    {
        var policy = new S7ReconnectPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0,
            JitterFactor = 0
        };

        Assert.Equal(TimeSpan.Zero, BackoffDelayCalculator.Calculate(1, policy));
        Assert.Equal(TimeSpan.FromSeconds(1), BackoffDelayCalculator.Calculate(2, policy));
        Assert.Equal(TimeSpan.FromSeconds(2), BackoffDelayCalculator.Calculate(3, policy));
        Assert.Equal(TimeSpan.FromSeconds(4), BackoffDelayCalculator.Calculate(4, policy));
        Assert.Equal(TimeSpan.FromSeconds(8), BackoffDelayCalculator.Calculate(5, policy));
        Assert.Equal(TimeSpan.FromSeconds(16), BackoffDelayCalculator.Calculate(6, policy));
        Assert.Equal(TimeSpan.FromSeconds(30), BackoffDelayCalculator.Calculate(7, policy));

        var jittered = policy with { JitterFactor = 0.2 };
        Assert.Equal(TimeSpan.FromSeconds(3.2), BackoffDelayCalculator.Calculate(4, jittered, () => 0.0));
        Assert.Equal(TimeSpan.FromSeconds(4.8), BackoffDelayCalculator.Calculate(4, jittered, () => 1.0));
    }

    [Fact]
    public async Task ConnectionLoss_ReconnectsAndReadRecovers()
    {
        var fake = new FakeTcpTransport();
        fake.Memory[0] = 0x12;
        fake.Memory[1] = 0x34;
        fake.Memory[2] = 0x56;
        fake.Memory[3] = 0x78;

        var states = new List<S7SessionState>();
        var reconnectKinds = new List<S7ReconnectEventKind>();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy()),
            transport: fake,
            transportFactory: () => fake);
        client.SessionStateChanged += (_, e) => states.Add(e.CurrentState);
        client.ReconnectEvent += (_, e) => reconnectKinds.Add(e.Kind);

        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        S7ReadResult result = await client.ReadBytesResultAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, result.Data.ToArray());
        Assert.Equal(1, result.RecoveryCount);
        Assert.True(result.Retried);
        Assert.Equal(2, fake.ConnectedCount);
        Assert.Equal(S7SessionState.Connected, client.State);
        Assert.Contains(S7SessionState.Reconnecting, states);
        Assert.Equal(
            new[] { S7ReconnectEventKind.AttemptStarted, S7ReconnectEventKind.Reconnected },
            reconnectKinds);
    }

    [Fact]
    public async Task TypedRead_CarriesRecoveryMetadata()
    {
        var fake = new FakeTcpTransport();
        fake.Memory[0] = 0x00;
        fake.Memory[1] = 0x2A;

        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy()),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        S7TypedReadResult result = await client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Int));

        Assert.Equal((short)42, result.Value.AsInt16());
        Assert.Equal(1, result.RecoveryCount);
        Assert.True(result.Retried);
    }

    [Fact]
    public async Task WaitTimeoutZero_ReturnsSessionReconnecting()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy(waitTimeout: TimeSpan.Zero)),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 1;
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesResultAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.SessionReconnecting, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Read, exception.Stage);
    }

    [Fact]
    public async Task ReconnectExhausted_Returns604AndDisconnects()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy(maxAttempts: 2)),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 2;
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBytesResultAsync(new S7ReadRequest(S7Area.DataBlock, 1, 0, 4)));

        Assert.Equal(S7ErrorCode.ReconnectExhausted, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Reconnect, exception.Stage);
        Assert.Equal(S7SessionState.Disconnected, client.State);
    }

    [Fact]
    public async Task UncertainWrite_IsNotReplayedByDefault()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy()),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailAfterRequestCount = 3;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.WriteBytesResultAsync(
                new S7WriteRequest(S7Area.DataBlock, 1, 0, new byte[] { 1, 2, 3, 4 })));

        Assert.Equal(S7ErrorCode.ConnectionReset, exception.ErrorCode);
        Assert.True(exception.WriteOutcomeUncertain);
        Assert.Equal(1, fake.ReceivedFrames.Count(frame => frame.Length > 17 && frame[17] == 0x05));
    }

    [Fact]
    public async Task Snapshot_ReportsReconnectingState()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy(maxAttempts: 0, initialDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(1))),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 1;
        fake.FailAfterRequestCount = 3;

        Task<S7ReadResult> readTask = client.ReadBytesResultAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);

        SessionSnapshot snapshot = client.Snapshot;
        Assert.Equal(S7SessionState.Reconnecting, snapshot.State);
        Assert.True(snapshot.AutoReconnectEnabled);
        Assert.Equal(Guid.Empty, snapshot.SessionId);
        Assert.Equal(0, snapshot.NegotiatedPduSize);
        Assert.True(snapshot.ReconnectAttempt >= 1);

        await client.DisconnectAsync();
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() => readTask);
        Assert.Equal(S7ErrorCode.ReconnectAborted, exception.ErrorCode);
    }

    [Fact]
    public async Task DisconnectWhileReconnecting_AbortsWaitWith605()
    {
        var fake = new FakeTcpTransport();
        await using S7Client client = new S7Client(
            CreateOptions(EnabledPolicy(
                maxAttempts: 0,
                initialDelay: TimeSpan.FromSeconds(1),
                maxDelay: TimeSpan.FromSeconds(1))),
            transport: fake,
            transportFactory: () => fake);
        await client.ConnectAsync();
        fake.FailConnectAttempts = 1;
        fake.FailAfterRequestCount = 3;

        Task<S7ReadResult> readTask = client.ReadBytesResultAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 4));
        await WaitUntilAsync(() => client.State == S7SessionState.Reconnecting);
        await client.DisconnectAsync();

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(() => readTask);
        Assert.Equal(S7ErrorCode.ReconnectAborted, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Reconnect, exception.Stage);
        Assert.Equal(S7SessionState.Disconnected, client.State);
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

    private static void AssertPolicyRejected(S7ReconnectPolicy policy, S7ErrorCode expectedCode)
    {
        var fake = new FakeTcpTransport();
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => new S7Client(CreateOptions(policy), transport: fake));
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
        Assert.Equal(0, fake.ConnectedCount);
    }

    private static S7ReconnectPolicy EnabledPolicy(
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        TimeSpan? waitTimeout = null,
        bool reconnectOnTimeout = false,
        bool replayUncertainWrites = false)
    {
        return new S7ReconnectPolicy
        {
            AutoReconnectEnabled = true,
            MaxAttempts = maxAttempts,
            InitialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100),
            MaxDelay = maxDelay ?? TimeSpan.FromSeconds(1),
            BackoffMultiplier = 1.0,
            JitterFactor = 0,
            WaitTimeout = waitTimeout ?? TimeSpan.FromSeconds(5),
            ReconnectOnTimeout = reconnectOnTimeout,
            ReplayUncertainWrites = replayUncertainWrites
        };
    }

    private static S7ConnectionOptions CreateOptions(S7ReconnectPolicy policy)
    {
        return new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ReadTimeout = TimeSpan.FromSeconds(2),
            WriteTimeout = TimeSpan.FromSeconds(2),
            RequestedPduSize = 480,
            Reconnect = policy
        };
    }
}