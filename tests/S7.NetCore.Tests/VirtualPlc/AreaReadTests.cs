using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class AreaReadTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task V_R01_FourAreas_SingleChunkReadIsStable()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        int readLength = pduSize - ProtocolConstants.ReadResponseOverhead;

        (S7Area Area, int DbNumber)[] targets =
        [
            (S7Area.Input, 0),
            (S7Area.Output, 0),
            (S7Area.Memory, 0),
            (S7Area.DataBlock, 1)
        ];

        foreach ((S7Area area, int dbNumber) in targets)
        {
            var request = new S7ReadRequest(area, dbNumber, 0, readLength);
            byte[] first = (await session.Client.ReadBytesAsync(request, BudgetToken)).ToArray();
            byte[] second = (await session.Client.ReadBytesAsync(request, BudgetToken)).ToArray();

            Assert.True(
                first.Length == readLength,
                $"{Describe(area, dbNumber, 0, readLength)}：返回长度 {first.Length} 与期望 {readLength} 不一致。");
            Assert.True(
                first.SequenceEqual(second),
                $"{Describe(area, dbNumber, 0, readLength)}：连续两次读取内容不一致。");
        }
    }

    [VirtualPlcFact]
    public async Task V_R02_Db1_1024Bytes_SplitsByNegotiatedPdu()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        int pduSize = session.Client.NegotiatedPduSize;
        int readLength = pduSize - ProtocolConstants.ReadResponseOverhead;

        ReadOnlyMemory<byte> data;
        try
        {
            data = await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, 1, 0, 1024),
                BudgetToken);
        }
        catch (S7ClientException ex) when (ex.PlcReturnCode == 0x05 && pduSize > 240)
        {
            Assert.Fail(
                $"协商 PDU={pduSize} 超出 6.8.7 的 222 字节单次读取基线，请将虚拟 PLC 配置为 PDU=240。"
                + $"原始错误：{ex.Message}");
            return;
        }

        ChunkAssertions.AssertReadChunks(
            session.Logger,
            pduSize,
            S7Area.DataBlock,
            1,
            0,
            1024,
            data,
            session.Transport.Frames);

        int expectedCount = (1024 + readLength - 1) / readLength;
        ChunkBoundary[] chunks = ChunkFrameInspector.ReadChunks(session.Transport.Frames).ToArray();
        Assert.Equal(expectedCount, chunks.Length);
        Assert.All(
            chunks,
            chunk => Assert.Equal((chunk.Index - 1) * readLength, chunk.StartByte));

        if (pduSize == 240)
        {
            Assert.Equal([0, 222, 444, 666, 888], chunks.Select(chunk => chunk.StartByte));
            Assert.Equal(5, chunks.Length);
        }
    }

    [VirtualPlcFact]
    public async Task V_R03_Db1_65535Bytes_SplitsAllChunks()
    {
        using CancellationTokenSource scope = CreateTimeoutScope(TimeSpan.FromSeconds(120));
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(scope.Token);

        int pduSize = session.Client.NegotiatedPduSize;
        int readLength = pduSize - ProtocolConstants.ReadResponseOverhead;

        ReadOnlyMemory<byte> data = await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 0, 65535),
            scope.Token);

        ChunkAssertions.AssertReadChunks(
            session.Logger,
            pduSize,
            S7Area.DataBlock,
            1,
            0,
            65535,
            data,
            session.Transport.Frames);

        int expectedCount = (65535 + readLength - 1) / readLength;
        Assert.Equal(expectedCount, ChunkFrameInspector.ReadChunks(session.Transport.Frames).Count);

        if (pduSize == 240)
        {
            Assert.Equal(296, expectedCount);
        }
    }

    [VirtualPlcFact]
    public async Task V_R04_Db1_TailRead2Bytes_Succeeds()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        ReadOnlyMemory<byte> data = await session.Client.ReadBytesAsync(
            new S7ReadRequest(S7Area.DataBlock, 1, 65533, 2),
            BudgetToken);

        Assert.Equal(2, data.Length);
    }

    [VirtualPlcFact]
    public async Task V_R05_Db2AndDb3_AreReadable()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        foreach (int dbNumber in new[] { 2, 3 })
        {
            ReadOnlyMemory<byte> data = await session.Client.ReadBytesAsync(
                new S7ReadRequest(S7Area.DataBlock, dbNumber, 0, 2),
                BudgetToken);

            Assert.True(
                data.Length == 2,
                $"{Describe(S7Area.DataBlock, dbNumber, 0, 2)}：返回长度 {data.Length} 与期望 2 不一致。");
        }
    }
}