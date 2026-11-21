using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 分包边界事实。
/// </summary>
internal readonly record struct ChunkBoundary(int Index, int StartByte, int Length);

/// <summary>
/// 位写目标事实。
/// </summary>
internal readonly record struct BitWriteBoundary(int Index, int StartByte, int BitNumber, bool Value);

/// <summary>
/// 从真实请求帧解析读/写分包边界，作为日志之外的第二事实来源。
/// </summary>
internal static class ChunkFrameInspector
{
    /// <summary>解析读请求帧。</summary>
    internal static IReadOnlyList<ChunkBoundary> ReadChunks(IReadOnlyList<RecordedFrame> frames)
    {
        var boundaries = new List<ChunkBoundary>();
        int index = 0;

        foreach (RecordedFrame frame in frames)
        {
            byte[] data = frame.Data;
            if (data.Length < ProtocolConstants.ReadRequestFrameSize
                || data[7] != ProtocolConstants.S7ProtocolId
                || data[17] != ProtocolConstants.S7FunctionRead)
            {
                continue;
            }

            index++;
            boundaries.Add(new ChunkBoundary(
                index,
                GetStartByte(data),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(23))));
        }

        return boundaries;
    }

    /// <summary>解析写请求帧。</summary>
    internal static IReadOnlyList<ChunkBoundary> WriteChunks(IReadOnlyList<RecordedFrame> frames)
    {
        var boundaries = new List<ChunkBoundary>();
        int index = 0;

        foreach (RecordedFrame frame in frames)
        {
            byte[] data = frame.Data;
            if (data.Length < ProtocolConstants.WriteRequestOverhead
                || data[7] != ProtocolConstants.S7ProtocolId
                || data[17] != ProtocolConstants.S7FunctionWrite
                || data[22] != ProtocolConstants.TransportSizeByte)
            {
                continue;
            }

            index++;
            boundaries.Add(new ChunkBoundary(
                index,
                GetStartByte(data),
                data.Length - ProtocolConstants.WriteRequestOverhead));
        }

        return boundaries;
    }

    /// <summary>解析单请求位写帧。</summary>
    internal static IReadOnlyList<BitWriteBoundary> BitWrites(IReadOnlyList<RecordedFrame> frames)
    {
        var boundaries = new List<BitWriteBoundary>();
        int index = 0;

        foreach (RecordedFrame frame in frames)
        {
            byte[] data = frame.Data;
            if (data.Length < ProtocolConstants.WriteBitRequestFrameSize
                || data[7] != ProtocolConstants.S7ProtocolId
                || data[17] != ProtocolConstants.S7FunctionWrite
                || data[22] != ProtocolConstants.BitTransportSizeParameter)
            {
                continue;
            }

            index++;
            int bitAddress = (data[28] << 16) | (data[29] << 8) | data[30];
            boundaries.Add(new BitWriteBoundary(
                index,
                bitAddress / 8,
                bitAddress % 8,
                (data[35] & 0x01) != 0));
        }

        return boundaries;
    }

    /// <summary>解析读请求帧（字节数组视图）。</summary>
    internal static IReadOnlyList<ChunkBoundary> ReadChunks(IReadOnlyList<byte[]> frames)
    {
        return ReadChunks(ToRecorded(frames));
    }

    /// <summary>解析写请求帧（字节数组视图）。</summary>
    internal static IReadOnlyList<ChunkBoundary> WriteChunks(IReadOnlyList<byte[]> frames)
    {
        return WriteChunks(ToRecorded(frames));
    }

    /// <summary>解析单请求位写帧（字节数组视图）。</summary>
    internal static IReadOnlyList<BitWriteBoundary> BitWrites(IReadOnlyList<byte[]> frames)
    {
        return BitWrites(ToRecorded(frames));
    }

    private static RecordedFrame[] ToRecorded(IReadOnlyList<byte[]> frames)
    {
        var recorded = new RecordedFrame[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            recorded[i] = new RecordedFrame(i, frames[i]);
        }

        return recorded;
    }

    private static int GetStartByte(ReadOnlySpan<byte> frame)
    {
        return ((frame[28] << 16) | (frame[29] << 8) | frame[30]) / 8;
    }
}