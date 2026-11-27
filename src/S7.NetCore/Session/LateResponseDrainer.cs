using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// 迟到响应排空结果。
/// </summary>
internal enum LateResponseDrainOutcome
{
    /// <summary>帧未发出，未触发排空。</summary>
    NotTriggered,

    /// <summary>窗口内命中并完整消费迟到响应。</summary>
    Drained,

    /// <summary>排空失败，会话已迁移为异常。</summary>
    Faulted,

    /// <summary>排空被并发断开/释放中止，或连接已被并发摘除。</summary>
    Aborted
}

/// <summary>
/// 单次失败排空尝试的度量信息，只存在于本次调用栈内。
/// </summary>
/// <param name="Outcome">排空结果。</param>
/// <param name="Window">排空窗口。</param>
/// <param name="Elapsed">实际排空耗时。</param>
internal readonly record struct DrainAttempt(
    LateResponseDrainOutcome Outcome,
    TimeSpan Window,
    TimeSpan Elapsed);

/// <summary>
/// 迟到响应清理：取消或超时后按 PDU Reference 消费在途响应。
/// </summary>
internal static class LateResponseDrainer
{
    /// <summary>
    /// 尝试消费迟到响应。
    /// </summary>
    /// <param name="transport">传输通道。</param>
    /// <param name="expectedPduReference">期望消费的 PDU Reference。</param>
    /// <param name="timeout">清理超时。</param>
    /// <param name="stage">失败阶段。</param>
    /// <param name="abortToken">会话级中止令牌（断开、释放或迁移时取消）。</param>
    /// <returns>成功消费返回 <see langword="true"/>。</returns>
    internal static async ValueTask<bool> TryDrainAsync(
        ITcpTransport transport,
        ushort expectedPduReference,
        TimeSpan timeout,
        S7OperationStage stage,
        CancellationToken abortToken = default)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, abortToken);

        try
        {
            while (true)
            {
                byte[] payload = await S7FrameReader
                    .ReadS7PayloadAsync(transport, stage, linkedSource.Token)
                    .ConfigureAwait(false);

                if (Matches(payload, expectedPduReference, stage))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (TransportException)
        {
            return false;
        }
        catch (S7ClientException)
        {
            return false;
        }
    }

    private static bool Matches(ReadOnlySpan<byte> payload, ushort expectedPduReference, S7OperationStage stage)
    {
        if (payload.Length < 13 || payload[0] != ProtocolConstants.S7ProtocolId)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(payload[4..]) != expectedPduReference)
        {
            return false;
        }

        byte expectedFunction = stage switch
        {
            S7OperationStage.Read => ProtocolConstants.S7FunctionRead,
            S7OperationStage.Write => ProtocolConstants.S7FunctionWrite,
            _ => 0
        };

        return expectedFunction != 0 && payload[12] == expectedFunction;
    }
}