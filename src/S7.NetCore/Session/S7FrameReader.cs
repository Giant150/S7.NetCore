namespace S7.NetCore.Internal;

/// <summary>
/// TPKT / COTP 帧读取与多段 TSDU 重组。
/// </summary>
internal static class S7FrameReader
{
    /// <summary>
    /// 读取一个完整 TPKT 帧。
    /// </summary>
    /// <param name="transport">传输通道。</param>
    /// <param name="stage">失败阶段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完整帧。</returns>
    internal static async ValueTask<byte[]> ReadFrameAsync(
        ITcpTransport transport,
        S7OperationStage stage,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[ProtocolConstants.TpktHeaderSize];
        await ExactReader.ReadExactAsync(transport, header, cancellationToken).ConfigureAwait(false);

        int length = TpktCodec.ReadLength(header, stage);
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await ExactReader.ReadExactAsync(transport, frame.AsMemory(ProtocolConstants.TpktHeaderSize), cancellationToken)
            .ConfigureAwait(false);

        return frame;
    }

    /// <summary>
    /// 读取完整 S7 净荷，必要时重组多个 COTP DT 分段。
    /// </summary>
    /// <param name="transport">传输通道。</param>
    /// <param name="stage">失败阶段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>S7 净荷。</returns>
    internal static async ValueTask<byte[]> ReadS7PayloadAsync(
        ITcpTransport transport,
        S7OperationStage stage,
        CancellationToken cancellationToken)
    {
        byte[] frame = await ReadFrameAsync(transport, stage, cancellationToken).ConfigureAwait(false);
        bool isLast = CotpCodec.IsLastDataUnit(frame, stage);
        if (isLast)
        {
            return CotpCodec.GetS7Payload(frame, stage).ToArray();
        }

        var assembled = new byte[ProtocolConstants.MaxFrameSize];
        int offset = 0;
        Append(assembled, ref offset, CotpCodec.GetS7Payload(frame, stage), stage);

        while (!isLast)
        {
            frame = await ReadFrameAsync(transport, stage, cancellationToken).ConfigureAwait(false);
            isLast = CotpCodec.IsLastDataUnit(frame, stage);
            Append(assembled, ref offset, CotpCodec.GetS7Payload(frame, stage), stage);
        }

        return assembled.AsSpan(0, offset).ToArray();
    }

    private static void Append(byte[] destination, ref int offset, ReadOnlySpan<byte> source, S7OperationStage stage)
    {
        if (offset + source.Length > destination.Length)
        {
            throw new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.ResponseTooShort,
                stage,
                $"COTP 分段重组超过上限 {destination.Length} 字节。");
        }

        source.CopyTo(destination.AsSpan(offset));
        offset += source.Length;
    }
}