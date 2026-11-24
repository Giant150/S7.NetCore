using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Batch;

/// <summary>批量请求校验与归一（字节视图 + 类型视图）。</summary>
public sealed class BatchRequestValidatorTest
{
    [Fact]
    public void BatchErrorCodes_HaveFrozenValues()
    {
        Assert.Equal(141, (int)S7ErrorCode.BatchItemsEmpty);
        Assert.Equal(142, (int)S7ErrorCode.BatchPduExceeded);
        Assert.Equal(143, (int)S7ErrorCode.BatchAddressConflict);
        Assert.Equal(144, (int)S7ErrorCode.BatchItemFailed);
        Assert.Equal(145, (int)S7ErrorCode.BatchItemUnsupported);
        Assert.Equal(146, (int)S7ErrorCode.BatchItemCountExceeded);
    }

    [Fact]
    public void ValidateRead_Empty_Throws141()
    {
        S7ClientException error = Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateRead([]));

        AssertFailure(error, S7ErrorCode.BatchItemsEmpty);
        Assert.Contains("至少需要 1 条", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRead_TooManyItems_Throws146WithBothLimits()
    {
        var items = new S7BatchReadItem[256];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new S7BatchReadItem(S7Area.DataBlock, 1, i, 1);
        }

        S7ClientException error = Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateRead(items));

        AssertFailure(error, S7ErrorCode.BatchItemCountExceeded);
        Assert.Contains("256", error.Message, StringComparison.Ordinal);
        Assert.Contains("255", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRead_ValidItems_NormalizesToWireItems()
    {
        S7BatchReadItem[] items =
        [
            new S7BatchReadItem(S7Area.Input, 0, 0, 4),
            new S7BatchReadItem(S7Area.DataBlock, 3, 10, 2)
        ];

        List<BatchWireItem> wires = BatchRequestValidator.ValidateRead(items);

        Assert.Equal(2, wires.Count);
        Assert.Equal(S7Area.Input, wires[0].Area);
        Assert.Equal(0, wires[0].DbNumber);
        Assert.Equal(0, wires[0].StartByte);
        Assert.Equal(4, wires[0].Length);
        Assert.False(wires[0].IsBit);
        Assert.Equal(S7Area.DataBlock, wires[1].Area);
        Assert.Equal(3, wires[1].DbNumber);
        Assert.Equal(10, wires[1].StartByte);
        Assert.Equal(2, wires[1].Length);
        Assert.All(wires, static wire => Assert.All(wire.Data.ToArray(), static b => Assert.Equal(0, b)));
    }

    [Theory]
    [InlineData(132, 1, 0, S7ErrorCode.InvalidLength)]
    [InlineData(132, 1, -1, S7ErrorCode.InvalidStartByte)]
    [InlineData(132, 1, 65536, S7ErrorCode.InvalidStartByte)]
    [InlineData(132, 0, 0, S7ErrorCode.InvalidDbNumber)]
    public void ValidateRead_InvalidParameters_SurfaceExistingErrorCodes(
        int areaValue,
        int dbNumber,
        int startByte,
        S7ErrorCode expected)
    {
        int length = expected == S7ErrorCode.InvalidLength ? 0 : 1;
        var item = new S7BatchReadItem((S7Area)areaValue, dbNumber, startByte, length);

        S7ClientException error = Assert.Throws<S7ClientException>(
            () => BatchRequestValidator.ValidateRead([item]));

        AssertFailure(error, expected);
    }

    [Fact]
    public void ValidateRead_UnknownArea_Throws120()
    {
        var item = new S7BatchReadItem((S7Area)0x85, 0, 0, 1);

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateRead([item])),
            S7ErrorCode.AreaNotSupported);
    }

    [Fact]
    public void ValidateWrite_InputArea_Throws120()
    {
        var item = new S7BatchWriteItem(S7Area.Input, 0, 0, new byte[] { 1 });

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateWrite([item])),
            S7ErrorCode.AreaNotSupported);
    }

    [Fact]
    public void ValidateWrite_EmptyData_Throws113()
    {
        var item = new S7BatchWriteItem(S7Area.DataBlock, 1, 0, ReadOnlyMemory<byte>.Empty);

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateWrite([item])),
            S7ErrorCode.InvalidData);
    }

    [Fact]
    public void ValidateWrite_OverlappingItems_Throws143()
    {
        S7BatchWriteItem[] items =
        [
            new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 1, 2 }),
            new S7BatchWriteItem(S7Area.DataBlock, 1, 1, new byte[] { 3, 4 })
        ];

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateWrite(items)),
            S7ErrorCode.BatchAddressConflict);
    }

    [Fact]
    public void ValidateWrite_CopiesCallerBufferIntoPrivateSnapshot()
    {
        byte[] buffer = [1, 2, 3, 4];
        S7BatchWriteItem[] items = [new S7BatchWriteItem(S7Area.DataBlock, 1, 0, buffer)];

        List<BatchWireItem> wires = BatchRequestValidator.ValidateWrite(items);

        buffer[0] = 9;
        Assert.Equal(1, wires[0].Data.Span[0]);
        Assert.Equal(4, wires[0].Length);
    }

    [Fact]
    public void ValidateTypedRead_NormalizesOccupiedBytesAndWire()
    {
        S7TypedReadBatchItem[] items =
        [
            new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int),
            new S7TypedReadBatchItem("DB1.DBX0.3", S7DataType.Bool),
            new S7TypedReadBatchItem("DB1.DBB10", S7DataType.String, 10),
            new S7TypedReadBatchItem("DB1.DBB20", S7DataType.S7String, 10),
            new S7TypedReadBatchItem("DB1.DBB40", S7DataType.S7WString, 10)
        ];

        List<BatchTypedReadPlan> plans = BatchRequestValidator.ValidateTypedRead(items);

        Assert.Equal(5, plans.Count);
        Assert.Equal(2, plans[0].BytesOccupied);
        Assert.Equal(S7DataType.Int, plans[0].DataType);
        Assert.Equal(2, plans[0].Wire.Length);
        Assert.Equal(S7Area.DataBlock, plans[0].Address.Area);
        Assert.Equal(1, plans[0].Address.DbNumber);
        Assert.Equal(0, plans[0].Address.StartByte);

        Assert.Equal(1, plans[1].BytesOccupied);
        Assert.Equal(3, plans[1].Address.BitNumber);
        Assert.Equal(1, plans[1].Wire.Length);

        Assert.Equal(10, plans[2].BytesOccupied);
        Assert.Equal(12, plans[3].BytesOccupied);
        Assert.Equal(24, plans[4].BytesOccupied);
    }

    [Fact]
    public void ValidateTypedRead_InvalidAddress_Throws130()
    {
        var item = new S7TypedReadBatchItem("DB1", S7DataType.Int);

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedRead([item])),
            S7ErrorCode.AddressSyntaxInvalid);
    }

    [Fact]
    public void ValidateTypedRead_NonStringWithCapacity_Throws136()
    {
        var item = new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int, 5);

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedRead([item])),
            S7ErrorCode.StringCapacityInvalid);
    }

    [Fact]
    public void ValidateTypedRead_TooManyItems_Throws146()
    {
        var items = new S7TypedReadBatchItem[256];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new S7TypedReadBatchItem("DB1.DBB" + i, S7DataType.Byte);
        }

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedRead(items)),
            S7ErrorCode.BatchItemCountExceeded);
    }

    [Fact]
    public void ValidateTypedWrite_BoolItem_BecomesBitWire()
    {
        S7TypedWriteBatchItem[] items =
        [
            new S7TypedWriteBatchItem("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)),
            new S7TypedWriteBatchItem("DB2.DBX1.4", S7DataType.Bool, S7Value.FromBool(false))
        ];

        List<BatchTypedWritePlan> plans = BatchRequestValidator.ValidateTypedWrite(items);

        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].Wire.IsBit);
        Assert.Equal(3, plans[0].Wire.BitNumber);
        Assert.Equal(1, plans[0].Wire.Length);
        Assert.Equal(1, plans[0].Wire.Data.Span[0]);
        Assert.Equal(1, plans[0].BytesOccupied);

        Assert.True(plans[1].Wire.IsBit);
        Assert.Equal(4, plans[1].Wire.BitNumber);
        Assert.Equal(1, plans[1].Wire.StartByte);
        Assert.Equal(0, plans[1].Wire.Data.Span[0]);
    }

    [Fact]
    public void ValidateTypedWrite_IntegerItem_EncodesBigEndian()
    {
        var item = new S7TypedWriteBatchItem("DB2.DBW4", S7DataType.Int, S7Value.FromInt16(-5));

        List<BatchTypedWritePlan> plans = BatchRequestValidator.ValidateTypedWrite([item]);

        Assert.False(plans[0].Wire.IsBit);
        Assert.Equal(2, plans[0].Wire.Length);
        Assert.Equal(new byte[] { 0xFF, 0xFB }, plans[0].Wire.Data.ToArray());
        Assert.Equal(2, plans[0].BytesOccupied);
    }

    [Fact]
    public void ValidateTypedWrite_InputArea_Throws120()
    {
        var item = new S7TypedWriteBatchItem("I0.0", S7DataType.Bool, S7Value.FromBool(true));

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedWrite([item])),
            S7ErrorCode.AreaNotSupported);
    }

    [Fact]
    public void ValidateTypedWrite_ValueKindMismatch_Throws138()
    {
        var item = new S7TypedWriteBatchItem("DB2.DBW0", S7DataType.Int, S7Value.FromBool(true));

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedWrite([item])),
            S7ErrorCode.ValueTypeMismatch);
    }

    [Fact]
    public void ValidateTypedWrite_OverlapOnSameBitByte_Throws143()
    {
        S7TypedWriteBatchItem[] items =
        [
            new S7TypedWriteBatchItem("DB2.DBX1.0", S7DataType.Bool, S7Value.FromBool(true)),
            new S7TypedWriteBatchItem("DB2.DBX1.5", S7DataType.Bool, S7Value.FromBool(true))
        ];

        AssertFailure(
            Assert.Throws<S7ClientException>(() => BatchRequestValidator.ValidateTypedWrite(items)),
            S7ErrorCode.BatchAddressConflict);
    }

    [Fact]
    public void ValidateTypedWrite_StringWithCapacity_EncodesRequestedEncoding()
    {
        var item = new S7TypedWriteBatchItem(
            "DB2.DBB10",
            S7DataType.S7String,
            S7Value.FromS7String("AB"),
            4);

        List<BatchTypedWritePlan> plans = BatchRequestValidator.ValidateTypedWrite([item]);

        Assert.Equal(6, plans[0].BytesOccupied);
        Assert.Equal(6, plans[0].Wire.Length);
        Assert.Equal(4, plans[0].Wire.Data.Span[0]);
        Assert.Equal(2, plans[0].Wire.Data.Span[1]);
        Assert.Equal((byte)'A', plans[0].Wire.Data.Span[2]);
        Assert.Equal((byte)'B', plans[0].Wire.Data.Span[3]);
    }

    private static void AssertFailure(S7ClientException error, S7ErrorCode expected)
    {
        Assert.Equal(expected, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.InvalidArgument, error.Category);
        Assert.Equal(S7OperationStage.Validate, error.Stage);
    }
}