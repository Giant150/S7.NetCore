using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 三方交叉校验：诊断日志、真实请求帧、公共返回值必须对同一分包操作给出一致事实。
/// </summary>
internal static class ChunkAssertions
{
    internal static void AssertReadChunks(
        TestLogger logger,
        int pduSize,
        S7Area area,
        int dbNumber,
        int startByte,
        int requestedLength,
        ReadOnlyMemory<byte> data,
        IReadOnlyList<RecordedFrame> frames)
    {
        int chunkSize = pduSize - ProtocolConstants.ReadResponseOverhead;
        int expectedCount = (requestedLength + chunkSize - 1) / chunkSize;

        Assert.Equal(requestedLength, data.Length);

        S7LogEntry[] logged = logger.Records
            .Where(record => record.Level == S7LogLevel.Debug
                && record.Entry.Operation == "Read"
                && record.Entry.Area == area
                && record.Entry.DbNumber == dbNumber)
            .Select(record => record.Entry)
            .OrderBy(entry => entry.ChunkIndex)
            .ToArray();

        IReadOnlyList<ChunkBoundary> frameChunks = ChunkFrameInspector.ReadChunks(frames);

        Assert.Equal(expectedCount, logged.Length);
        Assert.Equal(expectedCount, frameChunks.Count);

        for (int i = 0; i < expectedCount; i++)
        {
            int expectedStart = startByte + i * chunkSize;
            int expectedLength = Math.Min(chunkSize, requestedLength - i * chunkSize);

            Assert.Equal(i + 1, logged[i].ChunkIndex!.Value);
            Assert.Equal(expectedCount, logged[i].ChunkCount!.Value);
            Assert.Equal(expectedStart, logged[i].StartByte!.Value);
            Assert.Equal(expectedLength, logged[i].Length!.Value);

            Assert.Equal(i + 1, frameChunks[i].Index);
            Assert.Equal(expectedStart, frameChunks[i].StartByte);
            Assert.Equal(expectedLength, frameChunks[i].Length);
        }
    }

    internal static void AssertWriteChunks(
        TestLogger logger,
        int pduSize,
        S7Area area,
        int dbNumber,
        int startByte,
        int totalLength,
        int written,
        IReadOnlyList<RecordedFrame> frames)
    {
        int chunkSize = pduSize - ProtocolConstants.WriteRequestOverhead;
        int expectedCount = (totalLength + chunkSize - 1) / chunkSize;

        Assert.Equal(totalLength, written);

        S7LogEntry[] logged = logger.Records
            .Where(record => record.Level == S7LogLevel.Debug
                && record.Entry.Operation == "Write"
                && record.Entry.Area == area
                && record.Entry.DbNumber == dbNumber)
            .Select(record => record.Entry)
            .OrderBy(entry => entry.ChunkIndex)
            .ToArray();

        IReadOnlyList<ChunkBoundary> frameChunks = ChunkFrameInspector.WriteChunks(frames);

        Assert.Equal(expectedCount, logged.Length);
        Assert.True(
            frameChunks.Count >= expectedCount,
            $"真实写帧数量 {frameChunks.Count} 少于期望段数 {expectedCount}。");

        for (int i = 0; i < expectedCount; i++)
        {
            int expectedStart = startByte + i * chunkSize;
            int expectedLength = Math.Min(chunkSize, totalLength - i * chunkSize);

            Assert.Equal(i + 1, logged[i].ChunkIndex!.Value);
            Assert.Equal(expectedCount, logged[i].ChunkCount!.Value);
            Assert.Equal(expectedStart, logged[i].StartByte!.Value);
            Assert.Equal(expectedLength, logged[i].Length!.Value);

            Assert.Equal(i + 1, frameChunks[i].Index);
            Assert.Equal(expectedStart, frameChunks[i].StartByte);
            Assert.Equal(expectedLength, frameChunks[i].Length);
        }
    }

    internal static void AssertReadChunks(
        TestLogger logger,
        int pduSize,
        S7Address address,
        S7DataType dataType,
        int bytesOccupied,
        ReadOnlyMemory<byte> data,
        IReadOnlyList<RecordedFrame> frames)
    {
        try
        {
            AssertReadChunks(
                logger,
                pduSize,
                address.Area,
                address.DbNumber,
                address.StartByte,
                bytesOccupied,
                data,
                frames);
        }
        catch (Exception exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"类型化读取 '{address.Expression}'（{dataType}，占用 {bytesOccupied} 字节）分包断言失败：{exception.Message}");
        }
    }

    internal static void AssertWriteChunks(
        TestLogger logger,
        int pduSize,
        S7Address address,
        S7DataType dataType,
        int bytesOccupied,
        int written,
        IReadOnlyList<RecordedFrame> frames)
    {
        try
        {
            AssertWriteChunks(
                logger,
                pduSize,
                address.Area,
                address.DbNumber,
                address.StartByte,
                bytesOccupied,
                written,
                frames);
        }
        catch (Exception exception)
        {
            throw new Xunit.Sdk.XunitException(
                $"类型化写入 '{address.Expression}'（{dataType}，占用 {bytesOccupied} 字节）分包断言失败：{exception.Message}");
        }
    }
}