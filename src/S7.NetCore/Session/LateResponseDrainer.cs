using System.Buffers.Binary;

namespace S7.NetCore.Internal;

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
    /// <returns>成功消费返回 <see langword="true"/>。</returns>
    internal static async ValueTask<bool> TryDrainAsync(
        ITcpTransport transport,
        ushort expectedPduReference,
        TimeSpan timeout,
        S7OperationStage stage)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);

        try
        {
            while (true)
            {
                byte[] payload = await S7FrameReader
                    .ReadS7PayloadAsync(transport, stage, timeoutSource.Token)
                    .ConfigureAwait(false);

                if (Matches(payload, expectedPduReference))
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

    private static bool Matches(ReadOnlySpan<byte> payload, ushort expectedPduReference)
    {
        if (payload.Length < 6 || payload[0] != ProtocolConstants.S7ProtocolId)
        {
            return false;
        }

        return BinaryPrimitives.ReadUInt16BigEndian(payload[4..]) == expectedPduReference;
    }
}