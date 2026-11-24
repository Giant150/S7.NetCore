using System.Buffers.Binary;

namespace S7.NetCore.Internal;

/// <summary>
/// 批量读/写响应解析。读响应每项 4 字节结果头，写响应每项 1 字节返回码。
/// </summary>
internal static class S7BatchResponseParser
{
    internal static BatchReadRawItem[] ParseReadResponse(
        ReadOnlySpan<byte> payload,
        ushort expectedPduReference,
        IReadOnlyList<BatchWireItem> expectedItems)
    {
        S7AckHeader header = S7ResponseParser.ParseHeader(payload, expectedPduReference, S7OperationStage.Read);
        EnsureParameterHeader(payload, header, expectedItems.Count, ProtocolConstants.S7FunctionRead);

        int dataStart = ProtocolConstants.S7AckHeaderSize + header.ParameterLength;
        int cursor = dataStart;
        var results = new BatchReadRawItem[expectedItems.Count];

        for (int i = 0; i < expectedItems.Count; i++)
        {
            if (cursor + ProtocolConstants.ReadItemResultHeaderSize > payload.Length)
            {
                throw ProtocolError($"读取响应第 {i} 项结果头被截断。");
            }

            byte returnCode = payload[cursor];
            byte transportSize = payload[cursor + 1];
            int declaredLength = BinaryPrimitives.ReadUInt16BigEndian(payload[(cursor + 2)..]);
            int actualLength = transportSize == 0x03 ? (declaredLength + 7) / 8 : declaredLength / 8;
            int dataOffset = cursor + ProtocolConstants.ReadItemResultHeaderSize;

            if (dataOffset + actualLength > payload.Length)
            {
                throw ProtocolError($"读取响应第 {i} 项数据被截断。");
            }

            if (returnCode != ProtocolConstants.SuccessReturnCode)
            {
                (S7ErrorCode code, _) = PlcReturnCodeMapper.Map(returnCode);
                results[i] = new BatchReadRawItem(false, Array.Empty<byte>(), code, returnCode);
            }
            else if (actualLength < expectedItems[i].Length)
            {
                results[i] = new BatchReadRawItem(false, Array.Empty<byte>(), S7ErrorCode.ResponseTooShort, null);
            }
            else
            {
                var data = new byte[expectedItems[i].Length];
                payload.Slice(dataOffset, data.Length).CopyTo(data);
                results[i] = new BatchReadRawItem(true, data, null, null);
            }

            cursor = dataOffset + actualLength;
            if ((actualLength & 1) != 0)
            {
                cursor++;
            }
        }

        return results;
    }

    internal static BatchWriteRawItem[] ParseWriteResponse(
        ReadOnlySpan<byte> payload,
        ushort expectedPduReference,
        int expectedCount)
    {
        S7AckHeader header = S7ResponseParser.ParseHeader(payload, expectedPduReference, S7OperationStage.Write);
        EnsureParameterHeader(payload, header, expectedCount, ProtocolConstants.S7FunctionWrite);

        int dataStart = ProtocolConstants.S7AckHeaderSize + header.ParameterLength;
        if (header.DataLength < expectedCount || dataStart + expectedCount > payload.Length)
        {
            throw ProtocolError($"写入响应条目数不足：期望 {expectedCount}，数据区 {header.DataLength} 字节。");
        }

        var results = new BatchWriteRawItem[expectedCount];
        for (int i = 0; i < expectedCount; i++)
        {
            results[i] = new BatchWriteRawItem(payload[dataStart + i]);
        }

        return results;
    }

    private static void EnsureParameterHeader(
        ReadOnlySpan<byte> payload,
        S7AckHeader header,
        int expectedCount,
        byte expectedFunction)
    {
        if (header.ParameterLength < 2
            || payload.Length < ProtocolConstants.S7AckHeaderSize + 2)
        {
            throw ProtocolError("响应参数区长度不足。");
        }

        int functionOffset = ProtocolConstants.S7AckHeaderSize;
        int function = payload[functionOffset];
        int itemCount = payload[functionOffset + 1];
        if (function != expectedFunction)
        {
            throw ProtocolError($"响应功能码非法：0x{function:X2}，期望 0x{expectedFunction:X2}。");
        }

        if (itemCount != expectedCount)
        {
            throw ProtocolError($"响应条目数不匹配：期望 {expectedCount}，实际 {itemCount}。");
        }
    }

    private static S7ClientException ProtocolError(string message)
    {
        return new S7ClientException(
            S7ErrorCategory.Protocol,
            S7ErrorCode.InvalidS7Header,
            S7OperationStage.Read,
            message);
    }
}