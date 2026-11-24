using System.Buffers.Binary;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>多条目读/写响应解析：逐项定位、对齐、失败与协议异常。</summary>
public sealed class S7BatchResponseParserTest
{
    private const ushort Reference = 0x4242;
    private const byte ReadFunction = ProtocolConstants.S7FunctionRead;
    private const byte WriteFunction = ProtocolConstants.S7FunctionWrite;

    [Fact]
    public void ParseReadResponse_Success_ParsesEveryItemWithAlignment()
    {
        BatchWireItem[] items = [Wire(3), Wire(2)];
        byte[] payload = BuildPayload(
            Reference,
            ReadFunction,
            items.Length,
            DataItem(0xFF, 0x04, 24, [0xAA, 0xBB, 0xCC]),
            DataItem(0xFF, 0x04, 16, [0xDD, 0xEE]));

        BatchReadRawItem[] results = S7BatchResponseParser.ParseReadResponse(payload, Reference, items);

        Assert.Equal(2, results.Length);
        Assert.True(results[0].Succeeded);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, results[0].Data);
        Assert.Null(results[0].ErrorCode);
        Assert.Null(results[0].PlcReturnCode);

        Assert.True(results[1].Succeeded);
        Assert.Equal(new byte[] { 0xDD, 0xEE }, results[1].Data);
    }

    [Fact]
    public void ParseReadResponse_PlcReturnCode_FailsOnlyThatItem()
    {
        BatchWireItem[] items = [Wire(1), Wire(2)];
        byte[] payload = BuildPayload(
            Reference,
            ReadFunction,
            items.Length,
            DataItem(0xFF, 0x04, 8, [0x01]),
            DataItem(0x05, 0x04, 0, []));

        BatchReadRawItem[] results = S7BatchResponseParser.ParseReadResponse(payload, Reference, items);

        Assert.True(results[0].Succeeded);
        Assert.False(results[1].Succeeded);
        Assert.Empty(results[1].Data);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, results[1].ErrorCode);
        Assert.Equal(0x05, results[1].PlcReturnCode!.Value);
    }

    [Fact]
    public void ParseReadResponse_ShortData_ReportsResponseTooShort()
    {
        BatchWireItem[] items = [Wire(4)];
        byte[] payload = BuildPayload(
            Reference,
            ReadFunction,
            items.Length,
            DataItem(0xFF, 0x04, 16, [0x01, 0x02]));

        BatchReadRawItem[] results = S7BatchResponseParser.ParseReadResponse(payload, Reference, items);

        Assert.False(results[0].Succeeded);
        Assert.Equal(S7ErrorCode.ResponseTooShort, results[0].ErrorCode);
        Assert.Null(results[0].PlcReturnCode);
    }

    [Fact]
    public void ParseReadResponse_TruncatedData_ThrowsProtocolError()
    {
        BatchWireItem[] items = [Wire(4)];
        byte[] payload = BuildPayload(
            Reference,
            ReadFunction,
            items.Length,
            DataItem(0xFF, 0x04, 32, [0x01]));

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseReadResponse(payload, Reference, items));

        Assert.Equal(S7ErrorCode.InvalidS7Header, error.ErrorCode);
        Assert.Contains("截断", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseReadResponse_TruncatedResultHeader_ThrowsProtocolError()
    {
        BatchWireItem[] items = [Wire(1)];
        byte[] payload = BuildPayload(Reference, ReadFunction, 1);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseReadResponse(payload, Reference, items));

        Assert.Contains("结果头被截断", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseReadResponse_ItemCountMismatch_ThrowsProtocolError()
    {
        BatchWireItem[] items = [Wire(1), Wire(1)];
        byte[] payload = BuildPayload(
            Reference,
            ReadFunction,
            1,
            DataItem(0xFF, 0x04, 8, [0x01]));

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseReadResponse(payload, Reference, items));

        Assert.Contains("条目数不匹配", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseReadResponse_WrongFunction_ThrowsProtocolError()
    {
        BatchWireItem[] items = [Wire(1)];
        byte[] payload = BuildPayload(Reference, WriteFunction, 1);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseReadResponse(payload, Reference, items));

        Assert.Contains("功能码非法", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseReadResponse_WrongReference_ThrowsInvalidHeader()
    {
        BatchWireItem[] items = [Wire(1)];
        byte[] payload = BuildPayload((ushort)(Reference + 1), ReadFunction, 1);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseReadResponse(payload, Reference, items));

        Assert.Equal(S7ErrorCode.InvalidS7Header, error.ErrorCode);
    }

    [Fact]
    public void ParseWriteResponse_ReadsOneReturnCodePerItem()
    {
        byte[] payload = BuildPayload(Reference, WriteFunction, 2, 0xFF, 0x05);

        BatchWriteRawItem[] results = S7BatchResponseParser.ParseWriteResponse(payload, Reference, 2);

        Assert.Equal(2, results.Length);
        Assert.Equal(0xFF, results[0].ReturnCode);
        Assert.Equal(0x05, results[1].ReturnCode);
    }

    [Fact]
    public void ParseWriteResponse_InsufficientData_ThrowsProtocolError()
    {
        byte[] payload = BuildPayload(Reference, WriteFunction, 3, 0xFF);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseWriteResponse(payload, Reference, 3));

        Assert.Contains("条目数不足", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseWriteResponse_ItemCountMismatch_ThrowsProtocolError()
    {
        byte[] payload = BuildPayload(Reference, WriteFunction, 1, 0xFF);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseWriteResponse(payload, Reference, 2));

        Assert.Contains("条目数不匹配", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseWriteResponse_ParameterAreaTooShort_ThrowsProtocolError()
    {
        var items = new BatchWireItem[1];
        byte[] payload = BuildPayload(Reference, WriteFunction, 1, 0xFF);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 1);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => S7BatchResponseParser.ParseWriteResponse(payload, Reference, items.Length));

        Assert.Contains("参数区长度不足", error.Message, StringComparison.Ordinal);
    }

    private static BatchWireItem Wire(int length)
    {
        return new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[length]);
    }

    private static byte[] DataItem(byte returnCode, byte transportSize, int declaredBits, byte[] data)
    {
        byte[] item = new byte[4 + data.Length];
        item[0] = returnCode;
        item[1] = transportSize;
        BinaryPrimitives.WriteUInt16BigEndian(item.AsSpan(2), (ushort)declaredBits);
        data.CopyTo(item, 4);
        return item;
    }

    private static byte[] BuildPayload(ushort reference, byte function, int itemCount, params object[] parts)
    {
        var data = new List<byte>();
        foreach (object part in parts)
        {
            switch (part)
            {
                case byte[] bytes:
                    data.AddRange(bytes);
                    if ((bytes.Length & 1) != 0)
                    {
                        data.Add(0x00);
                    }

                    break;

                case byte single:
                    data.Add(single);
                    break;

                case int single:
                    data.Add((byte)single);
                    break;

                default:
                    throw new ArgumentException("不支持的响应片段类型。", nameof(parts));
            }
        }

        var payload = new byte[ProtocolConstants.S7AckHeaderSize + 2 + data.Count];
        payload[0] = ProtocolConstants.S7ProtocolId;
        payload[1] = ProtocolConstants.S7RosctrAckData;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), reference);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 2);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)data.Count);
        payload[12] = function;
        payload[13] = (byte)itemCount;
        data.CopyTo(payload, 14);
        return payload;
    }
}