using System.Diagnostics;
using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>批量写条目目标区间重叠检测。</summary>
public sealed class WriteOverlapDetectorTest
{
    [Fact]
    public void Validate_PartialIntersection_ThrowsConflict()
    {
        BatchWireItem[] items = [Wire(S7Area.DataBlock, 1, 0, 4), Wire(S7Area.DataBlock, 1, 2, 4)];

        S7ClientException error = Assert.Throws<S7ClientException>(() => WriteOverlapDetector.Validate(items));

        AssertConflict(error);
        Assert.Contains("0", error.Message, StringComparison.Ordinal);
        Assert.Contains("1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SameAddressTwice_ThrowsConflict()
    {
        BatchWireItem[] items = [Wire(S7Area.Memory, 0, 8, 2), Wire(S7Area.Memory, 0, 8, 2)];

        AssertConflict(Assert.Throws<S7ClientException>(() => WriteOverlapDetector.Validate(items)));
    }

    [Fact]
    public void Validate_ByteItemCoveringBitByte_ThrowsConflict()
    {
        BatchWireItem[] items =
        [
            Wire(S7Area.DataBlock, 1, 0, 4),
            new BatchWireItem(S7Area.DataBlock, 1, 2, new byte[] { 1 }, IsBit: true, BitNumber: 3)
        ];

        AssertConflict(Assert.Throws<S7ClientException>(() => WriteOverlapDetector.Validate(items)));
    }

    [Fact]
    public void Validate_TwoBitsOnSameByte_ThrowsConflict()
    {
        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 2, 5, new byte[] { 1 }, IsBit: true, BitNumber: 0),
            new BatchWireItem(S7Area.DataBlock, 2, 5, new byte[] { 0 }, IsBit: true, BitNumber: 7)
        ];

        AssertConflict(Assert.Throws<S7ClientException>(() => WriteOverlapDetector.Validate(items)));
    }

    [Fact]
    public void Validate_AdjacentRanges_DoNotConflict()
    {
        BatchWireItem[] items = [Wire(S7Area.DataBlock, 1, 0, 4), Wire(S7Area.DataBlock, 1, 4, 4)];

        WriteOverlapDetector.Validate(items);
    }

    [Fact]
    public void Validate_SameOffsetDifferentDb_DoNotConflict()
    {
        BatchWireItem[] items = [Wire(S7Area.DataBlock, 1, 0, 4), Wire(S7Area.DataBlock, 2, 0, 4)];

        WriteOverlapDetector.Validate(items);
    }

    [Fact]
    public void Validate_SameOffsetDifferentArea_DoNotConflict()
    {
        BatchWireItem[] items = [Wire(S7Area.DataBlock, 1, 0, 4), Wire(S7Area.Memory, 0, 0, 4)];

        WriteOverlapDetector.Validate(items);
    }

    [Fact]
    public void Validate_AdjacentBitBytes_DoNotConflict()
    {
        BatchWireItem[] items =
        [
            new BatchWireItem(S7Area.DataBlock, 1, 1, new byte[] { 1 }, IsBit: true, BitNumber: 7),
            new BatchWireItem(S7Area.DataBlock, 1, 2, new byte[] { 0 }, IsBit: true, BitNumber: 0)
        ];

        WriteOverlapDetector.Validate(items);
    }

    [Fact]
    public void Validate_EmptyList_DoesNotThrow()
    {
        WriteOverlapDetector.Validate([]);
    }

    [Fact]
    public void Validate_TwoHundredItemsIsLinearAndFast()
    {
        var items = new BatchWireItem[200];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = Wire(S7Area.DataBlock, 1, i * 4, 4);
        }

        var stopwatch = Stopwatch.StartNew();
        WriteOverlapDetector.Validate(items);
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"200 条目重叠检测耗时 {stopwatch.ElapsedMilliseconds} ms，疑似非线性实现。");
    }

    private static void AssertConflict(S7ClientException error)
    {
        Assert.Equal(S7ErrorCode.BatchAddressConflict, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.InvalidArgument, error.Category);
        Assert.Equal(S7OperationStage.Validate, error.Stage);
        Assert.Contains("重叠", error.Message, StringComparison.Ordinal);
    }

    private static BatchWireItem Wire(S7Area area, int dbNumber, int startByte, int length)
    {
        return new BatchWireItem(area, dbNumber, startByte, new byte[length]);
    }
}