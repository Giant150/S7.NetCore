using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 快照恢复结果。
/// </summary>
internal readonly record struct SnapshotRestoreResult(bool Succeeded, int StartByte, int Length, string? FailureReason);

/// <summary>
/// 现场快照与恢复守卫。
/// </summary>
internal static class RegionSnapshot
{
    /// <summary>
    /// 读取目标范围的现场快照。失败时直接抛出，调用方必须在任何写入之前终止。
    /// </summary>
    internal static async Task<byte[]> CaptureAsync(
        S7Client client,
        S7Area area,
        int dbNumber,
        int startByte,
        int length,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> data = await client
            .ReadBytesAsync(new S7ReadRequest(area, dbNumber, startByte, length), cancellationToken)
            .ConfigureAwait(false);

        return data.ToArray();
    }

    /// <summary>
    /// 写回快照并读回校验。仅连接/超时类失败重试一次，PLC 返回错误不重试。
    /// 方法本身不抛异常，恢复失败通过结果上报。
    /// </summary>
    internal static async Task<SnapshotRestoreResult> RestoreAndVerifyAsync(
        S7Client client,
        S7Area area,
        int dbNumber,
        int startByte,
        byte[] snapshot,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await client
                    .WriteBytesAsync(new S7WriteRequest(area, dbNumber, startByte, snapshot), cancellationToken)
                    .ConfigureAwait(false);

                ReadOnlyMemory<byte> readBack = await client
                    .ReadBytesAsync(new S7ReadRequest(area, dbNumber, startByte, snapshot.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (readBack.Span.SequenceEqual(snapshot))
                {
                    return new SnapshotRestoreResult(true, startByte, snapshot.Length, null);
                }

                return new SnapshotRestoreResult(
                    false,
                    startByte,
                    snapshot.Length,
                    $"恢复后读回内容与快照不一致（区域 {area}，DB {dbNumber}，起始 {startByte}，长度 {snapshot.Length}）。");
            }
            catch (S7ClientException ex) when (attempt == 0 && IsRetryable(ex))
            {
            }
            catch (S7ClientException ex)
            {
                return new SnapshotRestoreResult(
                    false,
                    startByte,
                    snapshot.Length,
                    $"{ex.Category}/{ex.ErrorCode}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new SnapshotRestoreResult(false, startByte, snapshot.Length, ex.Message);
            }
        }

        return new SnapshotRestoreResult(false, startByte, snapshot.Length, "恢复重试一次后仍失败。");
    }

    private static bool IsRetryable(S7ClientException exception)
    {
        return exception.Category is S7ErrorCategory.Connection or S7ErrorCategory.Timeout;
    }
}

/// <summary>
/// 现场恢复失败异常，携带未恢复区间与主体异常信息。
/// </summary>
internal sealed class SnapshotRestoreException : Exception
{
    internal SnapshotRestoreException(
        S7Area area,
        int dbNumber,
        int startByte,
        int length,
        string reason,
        Exception? bodyException)
        : base(
            $"现场恢复失败：区域 {area}，DB {dbNumber}，未恢复区间 [{startByte},{startByte + length})，原因：{reason}；"
            + $"主体异常：{Describe(bodyException)}。请人工核对虚拟 PLC 现场数据。",
            bodyException)
    {
    }

    private static string Describe(Exception? exception)
    {
        return exception is null ? "无" : $"{exception.GetType().Name}: {exception.Message}";
    }
}