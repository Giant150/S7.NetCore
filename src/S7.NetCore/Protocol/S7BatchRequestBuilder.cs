using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// 批量读/写请求帧构造。一次调用只构造一个请求帧。
/// </summary>
internal static class S7BatchRequestBuilder
{
    internal static byte[] BuildReadRequest(IReadOnlyList<BatchWireItem> items, ushort pduReference)
    {
        int parameterLength = 2 + (ProtocolConstants.ReadItemHeaderSize * items.Count);
        int totalLength = ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobHeaderSize
            + parameterLength;
        var frame = new byte[totalLength];
        WriteFixedHeader(frame, totalLength, parameterLength, dataLength: 0, pduReference, ProtocolConstants.S7FunctionRead, items.Count);

        int offset = ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobPayloadHeaderSize;
        foreach (BatchWireItem item in items)
        {
            WriteItemHeader(frame.AsSpan(offset, ProtocolConstants.ReadItemHeaderSize), item, isBit: false);
            offset += ProtocolConstants.ReadItemHeaderSize;
        }

        return frame;
    }

    internal static byte[] BuildWriteRequest(IReadOnlyList<BatchWireItem> items, ushort pduReference)
    {
        int parameterLength = 2 + (ProtocolConstants.ReadItemHeaderSize * items.Count);
        int dataLength = 0;
        for (int i = 0; i < items.Count; i++)
        {
            int length = items[i].Length;
            dataLength += ProtocolConstants.WriteItemDataHeaderSize + length;
            if ((length & 1) != 0 && i != items.Count - 1)
            {
                dataLength++;
            }
        }

        int totalLength = ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobHeaderSize
            + parameterLength
            + dataLength;
        var frame = new byte[totalLength];
        WriteFixedHeader(frame, totalLength, parameterLength, dataLength, pduReference, ProtocolConstants.S7FunctionWrite, items.Count);

        int parameterOffset = ProtocolConstants.TpktHeaderSize
            + ProtocolConstants.CotpDataHeaderSize
            + ProtocolConstants.S7JobPayloadHeaderSize;
        int dataOffset = parameterOffset + (parameterLength - 2);

        for (int i = 0; i < items.Count; i++)
        {
            BatchWireItem item = items[i];
            WriteItemHeader(frame.AsSpan(parameterOffset, ProtocolConstants.ReadItemHeaderSize), item, item.IsBit);
            parameterOffset += ProtocolConstants.ReadItemHeaderSize;

            frame[dataOffset] = 0x00;
            frame[dataOffset + 1] = item.IsBit
                ? ProtocolConstants.BitTransportSizeData
                : ProtocolConstants.TransportSizeOctet;
            BinaryPrimitives.WriteUInt16BigEndian(
                frame.AsSpan(dataOffset + 2),
                item.IsBit ? (ushort)1 : checked((ushort)(item.Length * 8)));
            dataOffset += ProtocolConstants.WriteItemDataHeaderSize;

            item.Data.Span.CopyTo(frame.AsSpan(dataOffset, item.Length));
            dataOffset += item.Length;
            if ((item.Length & 1) != 0 && i != items.Count - 1)
            {
                frame[dataOffset++] = 0x00;
            }
        }

        return frame;
    }

    private static void WriteFixedHeader(
        byte[] frame,
        int totalLength,
        int parameterLength,
        int dataLength,
        ushort pduReference,
        byte function,
        int itemCount)
    {
        TpktCodec.WriteHeader(frame, totalLength, function == ProtocolConstants.S7FunctionRead
            ? S7OperationStage.Read
            : S7OperationStage.Write);

        frame[4] = 0x02;
        frame[5] = ProtocolConstants.CotpPduTypeData;
        frame[6] = 0x80;

        frame[7] = ProtocolConstants.S7ProtocolId;
        frame[8] = ProtocolConstants.S7RosctrJob;
        frame[9] = 0x00;
        frame[10] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(11), pduReference);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(13), checked((ushort)parameterLength));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(15), checked((ushort)dataLength));

        frame[17] = function;
        frame[18] = checked((byte)itemCount);
    }

    private static void WriteItemHeader(Span<byte> destination, BatchWireItem item, bool isBit)
    {
        destination[0] = 0x12;
        destination[1] = 0x0A;
        destination[2] = 0x10;
        destination[3] = isBit ? ProtocolConstants.BitTransportSizeParameter : ProtocolConstants.TransportSizeByte;
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], checked((ushort)item.Length));
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], checked((ushort)item.DbNumber));
        destination[8] = (byte)item.Area;
        WriteBitAddress(destination[9..], item.StartByte, isBit ? item.BitNumber : 0);
    }

    private static void WriteBitAddress(Span<byte> destination, int startByte, int bitNumber)
    {
        int bitAddress = (startByte * 8) + bitNumber;
        destination[0] = (byte)(bitAddress >> 16);
        destination[1] = (byte)(bitAddress >> 8);
        destination[2] = (byte)bitAddress;
    }
}