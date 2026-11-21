using System.Runtime.ExceptionServices;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 类型化写用例在 DB2 的固定偏移段（spec 6.8.12）。
/// </summary>
internal static class TypedDb2Segments
{
    internal const int NumericStart = 0;
    internal const int NumericLength = 16;

    internal const int S7StringStart = 16;
    internal const int S7StringCapacity = 16;
    internal const int S7StringLength = 18;

    internal const int CStringStart = 40;
    internal const int CStringCapacity = 8;
    internal const int CStringLength = 8;

    internal const int BitStart = 4096;
    internal const int BitLength = 1;

    internal const int ChunkStringStart = 8192;
    internal const int ChunkStringCapacity = 250;
    internal const int ChunkStringLength = 252;

    internal const int WStringStart = 64;
    internal const int WStringCapacity = 16;
    internal const int WStringLength = 36;

    internal const int ChunkWStringStart = 8448;
    internal const int ChunkWStringCapacity = 250;
    internal const int ChunkWStringLength = 504;
}

/// <summary>
/// 类型化写用例的现场保护模板：按字节范围快照、写后恢复并校验。
/// </summary>
internal static class TypedWriteGuard
{
    internal static Task ExecuteAsync(
        S7Client client,
        int dbNumber,
        int startByte,
        int length,
        Func<Task> body,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(client, dbNumber, startByte, length, _ => body(), cancellationToken);
    }

    internal static async Task ExecuteAsync(
        S7Client client,
        int dbNumber,
        int startByte,
        int length,
        Func<byte[], Task> body,
        CancellationToken cancellationToken)
    {
        byte[] snapshot = await RegionSnapshot
            .CaptureAsync(client, S7Area.DataBlock, dbNumber, startByte, length, cancellationToken)
            .ConfigureAwait(false);

        Exception? bodyException = null;

        try
        {
            await body(snapshot).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            bodyException = exception;
        }

        SnapshotRestoreResult restore = await RegionSnapshot
            .RestoreAndVerifyAsync(
                client,
                S7Area.DataBlock,
                dbNumber,
                startByte,
                snapshot,
                CancellationToken.None)
            .ConfigureAwait(false);

        if (!restore.Succeeded)
        {
            throw new SnapshotRestoreException(
                S7Area.DataBlock,
                dbNumber,
                startByte,
                length,
                restore.FailureReason ?? "未知原因",
                bodyException);
        }

        if (bodyException is not null)
        {
            ExceptionDispatchInfo.Capture(bodyException).Throw();
        }
    }

    internal static string Describe(
        S7Area area,
        int dbNumber,
        int startByte,
        int? bitNumber,
        int bytesOccupied,
        S7ErrorCategory category,
        S7ErrorCode errorCode)
    {
        return $"区域 {area}，DB {dbNumber}，起始 {startByte}，位号 {bitNumber?.ToString() ?? "-"}，"
            + $"占用 {bytesOccupied}，错误 {category}/{errorCode}";
    }
}