using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
public sealed class ProtectionLayerTests
{
    [Fact]
    public void KnownPattern_UsesAbsoluteOffsetFormula()
    {
        byte[] pattern = KnownPattern.Create(100, 8);

        for (int j = 0; j < pattern.Length; j++)
        {
            Assert.Equal((byte)(((100 + j) * 7 + 11) & 0xFF), pattern[j]);
        }

        byte[] shifted = KnownPattern.Create(101, 8);
        Assert.Equal(pattern[1..], shifted[..7]);
    }

    [Fact]
    public async Task ProtectedWrite_RoundTripsAndRestoresSnapshot()
    {
        var inner = new FakeTcpTransport();
        var transport = new RecordingTcpTransport(inner);
        await using var client = new S7Client(CreateOptions(), transport: transport);
        await client.ConnectAsync();

        for (int i = 0; i < 256; i++)
        {
            inner.Memory[i] = (byte)(i ^ 0x5A);
        }

        byte[] snapshot = inner.Memory.AsSpan(0, 100).ToArray();

        await ProtectedWrite.WriteVerifyAndRestoreAsync(
            client,
            S7Area.DataBlock,
            2,
            0,
            100,
            CancellationToken.None);

        Assert.Equal(snapshot, inner.Memory.AsSpan(0, 100).ToArray());
        Assert.Equal(2, ChunkFrameInspector.WriteChunks(transport.Frames).Count);
    }

    [Fact]
    public async Task ProtectedWrite_SnapshotFailure_SendsNoWrite()
    {
        var inner = new FakeTcpTransport();
        var transport = new RecordingTcpTransport(inner);
        inner.ReadReturnCodeProvider = _ => 0x05;
        await using var client = new S7Client(CreateOptions(), transport: transport);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<S7ClientException>(
            () => ProtectedWrite.WriteVerifyAndRestoreAsync(
                client,
                S7Area.DataBlock,
                2,
                0,
                16,
                CancellationToken.None));

        Assert.Empty(ChunkFrameInspector.WriteChunks(transport.Frames));
    }

    [Fact]
    public async Task ProtectedWrite_RestoreFailure_ThrowsSnapshotRestoreException()
    {
        var inner = new FakeTcpTransport();
        var transport = new RecordingTcpTransport(inner);
        inner.WriteReturnCodeProvider = _ => 0x03;
        await using var client = new S7Client(CreateOptions(), transport: transport);
        await client.ConnectAsync();

        SnapshotRestoreException exception = await Assert.ThrowsAsync<SnapshotRestoreException>(
            () => ProtectedWrite.WriteVerifyAndRestoreAsync(
                client,
                S7Area.DataBlock,
                2,
                0,
                16,
                CancellationToken.None));

        Assert.Contains("未恢复区间", exception.Message, StringComparison.Ordinal);
        Assert.Contains("主体异常", exception.Message, StringComparison.Ordinal);
        Assert.IsType<S7ClientException>(exception.InnerException);
        Assert.Equal(2, ChunkFrameInspector.WriteChunks(transport.Frames).Count);
    }

    [Fact]
    public async Task ProtectedWrite_BodyFailure_RestoresAndRethrowsBodyException()
    {
        var inner = new FakeTcpTransport();
        var transport = new RecordingTcpTransport(inner);
        await using var client = new S7Client(CreateOptions(), transport: transport);
        await client.ConnectAsync();

        for (int i = 0; i < 64; i++)
        {
            inner.Memory[i] = (byte)i;
        }

        byte[] snapshot = inner.Memory.AsSpan(0, 16).ToArray();
        int readCount = 0;
        inner.ReadReturnCodeProvider = _ => Interlocked.Increment(ref readCount) == 2 ? (byte)0x05 : (byte)0xFF;

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => ProtectedWrite.WriteVerifyAndRestoreAsync(
                client,
                S7Area.DataBlock,
                2,
                0,
                16,
                CancellationToken.None));

        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, exception.ErrorCode);
        Assert.Equal(snapshot, inner.Memory.AsSpan(0, 16).ToArray());
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