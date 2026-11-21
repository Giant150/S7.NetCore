using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
public sealed class ObservationLayerTests
{
    [Fact]
    public async Task RecordingTransport_RecordsWritesOnly()
    {
        var inner = new FakeTcpTransport();
        var transport = new RecordingTcpTransport(inner);

        await transport.ConnectAsync("127.0.0.1", 1102, TimeSpan.FromSeconds(1), CancellationToken.None);

        byte[] firstFrame = S7RequestBuilder.BuildReadRequest(S7Area.Memory, 0, 0, 1, 1);
        byte[] secondFrame = S7RequestBuilder.BuildReadRequest(S7Area.Memory, 0, 1, 1, 2);
        await transport.WriteAsync(firstFrame, CancellationToken.None);
        await transport.WriteAsync(secondFrame, CancellationToken.None);

        Assert.Equal(2, transport.WriteCount);
        Assert.Equal(2, transport.Frames.Count);
        Assert.Equal(0, transport.Frames[0].Index);
        Assert.Equal(firstFrame, transport.Frames[0].Data);
        Assert.Equal(1, transport.Frames[1].Index);
        Assert.Equal(secondFrame, transport.Frames[1].Data);

        var buffer = new byte[128];
        int read = await transport.ReadAsync(buffer, CancellationToken.None);

        Assert.True(read > 0);
        Assert.Equal(2, transport.Frames.Count);

        await transport.DisposeAsync();
        await transport.DisposeAsync();
        Assert.False(transport.IsOpen);
    }

    [Fact]
    public void ChunkFrameInspector_ReadsAndWritesMatchPdu240Baseline()
    {
        var frames = new List<RecordedFrame>
        {
            new(0, CotpCodec.BuildConnectionRequest(new Tsap(0x01, 0x00), new Tsap(0x03, 0x01), S7OperationStage.CotpConnect)),
            new(1, S7RequestBuilder.BuildSetupCommunication(480, 1))
        };

        ushort reference = 2;
        for (int offset = 0; offset < 1024; offset += 222)
        {
            int length = Math.Min(222, 1024 - offset);
            frames.Add(new RecordedFrame(
                reference,
                S7RequestBuilder.BuildReadRequest(S7Area.DataBlock, 1, offset, length, reference)));
            reference++;
        }

        for (int offset = 0; offset < 1024; offset += 205)
        {
            int length = Math.Min(205, 1024 - offset);
            frames.Add(new RecordedFrame(
                reference,
                S7RequestBuilder.BuildWriteRequest(S7Area.DataBlock, 2, offset, new byte[length], reference)));
            reference++;
        }

        IReadOnlyList<ChunkBoundary> readChunks = ChunkFrameInspector.ReadChunks(frames);
        IReadOnlyList<ChunkBoundary> writeChunks = ChunkFrameInspector.WriteChunks(frames);

        Assert.Equal(5, readChunks.Count);
        Assert.Equal([0, 222, 444, 666, 888], readChunks.Select(chunk => chunk.StartByte));
        Assert.Equal([222, 222, 222, 222, 136], readChunks.Select(chunk => chunk.Length));

        Assert.Equal(5, writeChunks.Count);
        Assert.Equal([0, 205, 410, 615, 820], writeChunks.Select(chunk => chunk.StartByte));
        Assert.Equal([205, 205, 205, 205, 204], writeChunks.Select(chunk => chunk.Length));

        Assert.All(readChunks.Select(chunk => chunk.Index), index => Assert.InRange(index, 1, 5));
        Assert.Equal(
            ProtocolConstants.ReadResponseOverhead,
            240 - 222);
        Assert.Equal(
            ProtocolConstants.WriteRequestOverhead,
            240 - 205);
    }

    [Fact]
    public void ChunkFrameInspector_ExcludesConnectionAndSetupFrames()
    {
        var frames = new List<RecordedFrame>
        {
            new(0, CotpCodec.BuildConnectionRequest(new Tsap(0x01, 0x00), new Tsap(0x03, 0x01), S7OperationStage.CotpConnect)),
            new(1, S7RequestBuilder.BuildSetupCommunication(480, 1)),
            new(2, S7RequestBuilder.BuildReadRequest(S7Area.Memory, 0, 0, 8, 2))
        };

        Assert.Empty(ChunkFrameInspector.WriteChunks(frames));
        Assert.Single(ChunkFrameInspector.ReadChunks(frames));
    }

    [Fact]
    public void ChunkFrameInspector_SeparatesBitWritesFromByteWrites()
    {
        var frames = new List<RecordedFrame>
        {
            new(0, S7RequestBuilder.BuildWriteBitRequest(S7Area.DataBlock, 2, 0, 3, true, 1)),
            new(1, S7RequestBuilder.BuildWriteRequest(S7Area.DataBlock, 2, 0, new byte[4], 2))
        };

        Assert.Single(ChunkFrameInspector.WriteChunks(frames));

        BitWriteBoundary[] bitWrites = ChunkFrameInspector.BitWrites(frames).ToArray();
        Assert.Single(bitWrites);
        Assert.Equal(0, bitWrites[0].StartByte);
        Assert.Equal(3, bitWrites[0].BitNumber);
        Assert.True(bitWrites[0].Value);
    }
}