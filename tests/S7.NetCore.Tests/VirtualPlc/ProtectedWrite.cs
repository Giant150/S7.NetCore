using System.Runtime.ExceptionServices;
using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 写用例现场保护模板：快照 → 写入已知模式 → 读回校验 → 恢复并校验。
/// 主体异常与恢复异常互不覆盖。
/// </summary>
internal static class ProtectedWrite
{
    internal static async Task WriteVerifyAndRestoreAsync(
        S7Client client,
        S7Area area,
        int dbNumber,
        int startByte,
        int length,
        CancellationToken cancellationToken)
    {
        byte[] snapshot = await RegionSnapshot
            .CaptureAsync(client, area, dbNumber, startByte, length, cancellationToken)
            .ConfigureAwait(false);

        byte[] pattern = KnownPattern.Create(startByte, length);
        Exception? bodyException = null;

        try
        {
            int written = await client
                .WriteBytesAsync(new S7WriteRequest(area, dbNumber, startByte, pattern), cancellationToken)
                .ConfigureAwait(false);

            if (written != length)
            {
                throw new InvalidOperationException(
                    $"写入字节数 {written} 与请求长度 {length} 不一致（区域 {area}，DB {dbNumber}，起始 {startByte}）。");
            }

            ReadOnlyMemory<byte> readBack = await client
                .ReadBytesAsync(new S7ReadRequest(area, dbNumber, startByte, length), cancellationToken)
                .ConfigureAwait(false);

            if (!readBack.Span.SequenceEqual(pattern))
            {
                throw new InvalidOperationException(
                    $"写入回环不一致：区域 {area}，DB {dbNumber}，起始 {startByte}，长度 {length}；"
                    + "虚拟 PLC 可能被其他客户端占用。");
            }
        }
        catch (Exception ex)
        {
            bodyException = ex;
        }

        SnapshotRestoreResult restore = await RegionSnapshot
            .RestoreAndVerifyAsync(client, area, dbNumber, startByte, snapshot, CancellationToken.None)
            .ConfigureAwait(false);

        if (!restore.Succeeded)
        {
            throw new SnapshotRestoreException(
                area,
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
}