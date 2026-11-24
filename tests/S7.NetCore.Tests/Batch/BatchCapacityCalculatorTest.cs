using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>批量净荷容量计算的边界与口径。</summary>
public sealed class BatchCapacityCalculatorTest
{
    private const byte LengthOne = 1;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(255, 256)]
    public void Align_RoundsUpToEven(int length, int expected)
    {
        Assert.Equal(expected, BatchCapacityCalculator.Align(length));
    }

    [Fact]
    public void ReadRequestRequired_IsJobHeaderPlusTwelvePerItem()
    {
        int header = ProtocolConstants.S7JobPayloadHeaderSize;
        int itemHeader = ProtocolConstants.ReadItemHeaderSize;

        Assert.Equal(header, BatchCapacityCalculator.ReadRequestRequired(0));
        Assert.Equal(header + itemHeader, BatchCapacityCalculator.ReadRequestRequired(1));
        Assert.Equal(header + (2 * itemHeader), BatchCapacityCalculator.ReadRequestRequired(2));
        Assert.Equal(header + (255 * itemHeader), BatchCapacityCalculator.ReadRequestRequired(255));
    }

    [Fact]
    public void ReadResponseRequired_UsesFourByteResultHeaderAndEvenAlignment()
    {
        BatchWireItem[] items = [Wire(4), Wire(1), Wire(3)];

        long expected = ProtocolConstants.S7AckPayloadHeaderSize
            + (ProtocolConstants.ReadItemResultHeaderSize + 4)
            + (ProtocolConstants.ReadItemResultHeaderSize + 2)
            + (ProtocolConstants.ReadItemResultHeaderSize + 4);

        Assert.Equal(expected, BatchCapacityCalculator.ReadResponseRequired(items));
        Assert.Equal(ProtocolConstants.S7AckPayloadHeaderSize, BatchCapacityCalculator.ReadResponseRequired([]));
    }

    [Fact]
    public void WriteRequestRequired_CountsItemHeaderDataHeaderAndAlignment()
    {
        BatchWireItem[] items = [Wire(1), Wire(4)];

        long expected = ProtocolConstants.S7JobPayloadHeaderSize
            + (ProtocolConstants.ReadItemHeaderSize + ProtocolConstants.WriteItemDataHeaderSize + 2)
            + (ProtocolConstants.ReadItemHeaderSize + ProtocolConstants.WriteItemDataHeaderSize + 4);

        Assert.Equal(expected, BatchCapacityCalculator.WriteRequestRequired(items));
    }

    [Fact]
    public void WriteResponseRequired_IsAckHeaderPlusOneBytePerItem()
    {
        Assert.Equal(
            ProtocolConstants.S7AckPayloadHeaderSize + 1,
            BatchCapacityCalculator.WriteResponseRequired(1));
        Assert.Equal(
            ProtocolConstants.S7AckPayloadHeaderSize + 255,
            BatchCapacityCalculator.WriteResponseRequired(255));

        for (int count = 1; count <= 255; count++)
        {
            long writeResponse = BatchCapacityCalculator.WriteResponseRequired(count);
            long writeRequest = BatchCapacityCalculator.WriteRequestRequired(Wires(count, 1));
            Assert.True(writeResponse < writeRequest);
        }
    }

    [Fact]
    public void ValidateRead_RequestSideExactFitPasses()
    {
        BatchWireItem[] items = [Wire(4), Wire(4)];
        int request = (int)BatchCapacityCalculator.ReadRequestRequired(items.Length);
        int response = (int)BatchCapacityCalculator.ReadResponseRequired(items);
        Assert.True(request > response);

        BatchCapacityCalculator.ValidateRead(items, request);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => BatchCapacityCalculator.ValidateRead(items, request - 1));
        AssertBatchPduExceeded(error);
        Assert.Contains("请求侧", error.Message, StringComparison.Ordinal);
        Assert.Contains(request.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Contains((request - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRead_ResponseSideExactFitPasses()
    {
        BatchWireItem[] items = [Wire(20), Wire(20)];
        int request = (int)BatchCapacityCalculator.ReadRequestRequired(items.Length);
        int response = (int)BatchCapacityCalculator.ReadResponseRequired(items);
        Assert.True(response > request);

        BatchCapacityCalculator.ValidateRead(items, response);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => BatchCapacityCalculator.ValidateRead(items, response - 1));
        AssertBatchPduExceeded(error);
        Assert.Contains("响应侧", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRead_BothSidesExceeded_ReportsBothSides()
    {
        BatchWireItem[] items = [Wire(20), Wire(20)];

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => BatchCapacityCalculator.ValidateRead(items, 20));

        AssertBatchPduExceeded(error);
        Assert.Contains("请求侧与响应侧", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRead_UnknownPduSizeSkipsCapacityCheck()
    {
        BatchWireItem[] items = [Wire(1000)];

        BatchCapacityCalculator.ValidateRead(items, 0);
        BatchCapacityCalculator.ValidateRead(items, -1);

        Assert.Throws<S7ClientException>(() => BatchCapacityCalculator.ValidateRead(items, 240));
    }

    [Fact]
    public void ValidateWrite_ExactFitPassesAndOneByteLessFails()
    {
        BatchWireItem[] items = [Wire(1)];
        int required = (int)BatchCapacityCalculator.WriteRequestRequired(items);
        Assert.Equal(ProtocolConstants.S7JobPayloadHeaderSize
            + ProtocolConstants.ReadItemHeaderSize
            + ProtocolConstants.WriteItemDataHeaderSize
            + 2, required);

        BatchCapacityCalculator.ValidateWrite(items, required);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => BatchCapacityCalculator.ValidateWrite(items, required - 1));
        AssertBatchPduExceeded(error);
        Assert.Contains("请求侧", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWrite_UnknownPduSizeSkipsCapacityCheck()
    {
        BatchWireItem[] items = [Wire(1000)];

        BatchCapacityCalculator.ValidateWrite(items, 0);

        Assert.Throws<S7ClientException>(() => BatchCapacityCalculator.ValidateWrite(items, 240));
    }

    [Fact]
    public void PduExceeded_MessageCarriesRegroupHint()
    {
        S7ClientException error = BatchCapacityCalculator.PduExceeded("请求侧", 300, 240);

        Assert.Equal(S7ErrorCode.BatchPduExceeded, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.InvalidArgument, error.Category);
        Assert.Equal(S7OperationStage.Validate, error.Stage);
        Assert.Contains("300", error.Message, StringComparison.Ordinal);
        Assert.Contains("240", error.Message, StringComparison.Ordinal);
        Assert.Contains("分组", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaxBatchItemCount_IsProtocolByteLimit()
    {
        Assert.Equal(255, ProtocolConstants.MaxBatchItemCount);
        for (int count = 1; count <= 255; count++)
        {
            Assert.True(BatchCapacityCalculator.ReadRequestRequired(count) > 0);
        }
    }

    private static void AssertBatchPduExceeded(S7ClientException error)
    {
        Assert.Equal(S7ErrorCode.BatchPduExceeded, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.InvalidArgument, error.Category);
        Assert.Equal(S7OperationStage.Validate, error.Stage);
    }

    private static BatchWireItem Wire(int length)
    {
        return new BatchWireItem(S7Area.DataBlock, 1, 0, new byte[length]);
    }

    private static BatchWireItem[] Wires(int count, int length)
    {
        var wires = new BatchWireItem[count];
        for (int i = 0; i < count; i++)
        {
            wires[i] = new BatchWireItem(S7Area.DataBlock, 1, i * length, new byte[length]);
        }

        return wires;
    }
}