using S7.NetCore;

namespace S7.NetCore.Tests.Batch;

/// <summary>批量门面端到端：一次请求帧、逐条结果、失败明细与类型化解码。</summary>
public sealed class S7ClientTest
{
    [Fact]
    public async Task ReadBatchAsync_ReturnsDataInRequestOrderWithSingleReadFrame()
    {
        var simulator = new BatchPlcSimulator();
        simulator.Memory[0] = 0x11;
        simulator.Memory[1] = 0x22;
        simulator.Memory[2] = 0x33;
        simulator.Memory[3] = 0x44;
        simulator.Memory[100] = 0x99;
        simulator.Memory[101] = 0x88;

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchReadResult result = await client.ReadBatchAsync(
        [
            new S7BatchReadItem(S7Area.DataBlock, 1, 0, 4),
            new S7BatchReadItem(S7Area.DataBlock, 1, 100, 2)
        ]);

        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(0, result.Items[0].Index);
        Assert.True(result.Items[0].Succeeded);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, result.Items[0].Data.ToArray());
        Assert.Null(result.Items[0].ErrorCode);
        Assert.Null(result.Items[0].PlcReturnCode);
        Assert.Equal(1, result.Items[1].Index);
        Assert.Equal(new byte[] { 0x99, 0x88 }, result.Items[1].Data.ToArray());
        Assert.Equal(1, simulator.ReadFrameCount);
        Assert.Equal(0, simulator.WriteFrameCount);

        result.EnsureAllSucceeded();
    }

    [Fact]
    public async Task ReadBatchAsync_PlcItemFailure_DoesNotThrowAndReportsFailure()
    {
        var simulator = new BatchPlcSimulator
        {
            ReadItemProvider = index => index == 1 ? ((byte)0x05, 0) : (ProtocolConstantsSuccess, 8)
        };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchReadResult result = await client.ReadBatchAsync(
        [
            new S7BatchReadItem(S7Area.DataBlock, 1, 0, 1),
            new S7BatchReadItem(S7Area.DataBlock, 1, 1, 1)
        ]);

        Assert.False(result.AllSucceeded);
        Assert.True(result.Items[0].Succeeded);
        Assert.False(result.Items[1].Succeeded);
        Assert.True(result.Items[1].Data.IsEmpty);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, result.Items[1].ErrorCode!.Value);
        Assert.Equal(0x05, result.Items[1].PlcReturnCode!.Value);

        S7BatchItemFailure failure = Assert.Single(result.Failures);
        Assert.Equal(1, failure.Index);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, failure.ErrorCode);
        Assert.Equal(0x05, failure.PlcReturnCode!.Value);
    }

    [Fact]
    public async Task ReadBatchAsync_EnsureAllSucceeded_Throws144WithProgress()
    {
        var simulator = new BatchPlcSimulator
        {
            ReadItemProvider = index => index == 1 ? ((byte)0x05, 0) : (ProtocolConstantsSuccess, 8)
        };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchReadResult result = await client.ReadBatchAsync(
        [
            new S7BatchReadItem(S7Area.DataBlock, 1, 0, 1),
            new S7BatchReadItem(S7Area.DataBlock, 1, 1, 1)
        ]);

        S7ClientException error = Assert.Throws<S7ClientException>(result.EnsureAllSucceeded);

        Assert.Equal(S7ErrorCode.BatchItemFailed, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.PlcReturn, error.Category);
        Assert.Equal(S7OperationStage.Read, error.Stage);
        Assert.Single(error.BatchFailedItems);
        Assert.Equal(1, error.SucceededItemCount);
        Assert.Null(error.WrittenRange);
        Assert.Contains("1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteBatchAsync_WritesAllItemsAndReportsBytesWritten()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchWriteResult result = await client.WriteBatchAsync(
        [
            new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 0x01, 0x02, 0x03 }),
            new S7BatchWriteItem(S7Area.Memory, 0, 10, new byte[] { 0xAA, 0xBB })
        ]);

        Assert.True(result.AllSucceeded);
        Assert.Empty(result.Failures);
        Assert.Equal(5, result.BytesWritten);
        Assert.Equal(3, result.Items[0].BytesWritten);
        Assert.Equal(2, result.Items[1].BytesWritten);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, simulator.Memory[..3]);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, simulator.Memory[10..12]);
        Assert.Equal(1, simulator.WriteFrameCount);
        Assert.Equal(0, simulator.ReadFrameCount);
    }

    [Fact]
    public async Task WriteBatchAsync_ItemReturnCode_ReportsItemFailureOnly()
    {
        var simulator = new BatchPlcSimulator
        {
            WriteReturnCodeProvider = index => index == 1 ? (byte)0x05 : ProtocolConstantsSuccess
        };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchWriteResult result = await client.WriteBatchAsync(
        [
            new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 0x01, 0x02 }),
            new S7BatchWriteItem(S7Area.DataBlock, 1, 10, new byte[] { 0x03, 0x04 })
        ]);

        Assert.False(result.AllSucceeded);
        Assert.True(result.Items[0].Succeeded);
        Assert.Equal(2, result.Items[0].BytesWritten);
        Assert.False(result.Items[1].Succeeded);
        Assert.Equal(0, result.Items[1].BytesWritten);
        Assert.Equal(S7ErrorCode.PlcAddressOutOfRange, result.Items[1].ErrorCode);
        Assert.Equal(2, result.BytesWritten);
        Assert.Single(result.Failures);
        Assert.Equal(1, result.Failures[0].Index);
    }

    [Fact]
    public async Task WriteBatchAsync_EnsureAllSucceeded_Throws144WithBytesWritten()
    {
        var simulator = new BatchPlcSimulator { WriteReturnCodeProvider = index => index == 0 ? (byte)0x05 : ProtocolConstantsSuccess };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7BatchWriteResult result = await client.WriteBatchAsync(
        [
            new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 0x01 }),
            new S7BatchWriteItem(S7Area.DataBlock, 1, 4, new byte[] { 0x02 })
        ]);

        S7ClientException error = Assert.Throws<S7ClientException>(result.EnsureAllSucceeded);

        Assert.Equal(S7ErrorCode.BatchItemFailed, error.ErrorCode);
        Assert.Equal(S7OperationStage.Write, error.Stage);
        Assert.Equal(1, error.SucceededItemCount);
        Assert.Equal(1, error.BytesWritten);
        Assert.False(error.WriteOutcomeUncertain);
    }

    [Fact]
    public async Task ReadTypedBatchAsync_DecodesValuesAndMarksStringEncoding()
    {
        var simulator = new BatchPlcSimulator();
        simulator.Memory[0] = 0x04;
        simulator.Memory[1] = 0xD2;
        simulator.Memory[4] = 0x3F;
        simulator.Memory[5] = 0xC0;
        simulator.Memory[6] = 0x00;
        simulator.Memory[7] = 0x00;
        simulator.Memory[8] = 0b0000_1000;
        simulator.Memory[200] = 4;
        simulator.Memory[201] = 2;
        simulator.Memory[202] = (byte)'A';
        simulator.Memory[203] = (byte)'B';
        simulator.Memory[210] = (byte)'C';
        simulator.Memory[211] = (byte)'D';
        simulator.Memory[212] = 0x00;
        simulator.Memory[220] = 0x00;
        simulator.Memory[221] = 0x02;
        simulator.Memory[222] = 0x00;
        simulator.Memory[223] = 0x01;
        simulator.Memory[224] = 0x00;
        simulator.Memory[225] = (byte)'X';

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7TypedBatchReadResult result = await client.ReadTypedBatchAsync(
        [
            new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int),
            new S7TypedReadBatchItem("DB1.DBD4", S7DataType.Real),
            new S7TypedReadBatchItem("DB1.DBX8.3", S7DataType.Bool),
            new S7TypedReadBatchItem("DB1.DBB200", S7DataType.S7String, 4),
            new S7TypedReadBatchItem("DB1.DBB210", S7DataType.String, 4),
            new S7TypedReadBatchItem("DB1.DBB220", S7DataType.S7WString, 2)
        ]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(6, result.Items.Count);
        Assert.Equal((short)1234, result.Items[0].Value.AsInt16());
        Assert.Equal(2, result.Items[0].BytesOccupied);
        Assert.Equal(S7Area.DataBlock, result.Items[0].Address.Area);
        Assert.Equal(1, result.Items[0].Address.DbNumber);

        Assert.Equal(1.5f, result.Items[1].Value.AsSingle());
        Assert.Equal(4, result.Items[1].BytesOccupied);

        Assert.True(result.Items[2].Value.AsBoolean());
        Assert.Equal(3, result.Items[2].Address.BitNumber);
        Assert.Equal(1, result.Items[2].BytesOccupied);

        Assert.Equal("AB", result.Items[3].Value.AsString());
        Assert.Equal(S7DataType.S7String, result.Items[3].Value.StringEncoding);
        Assert.Equal(6, result.Items[3].BytesOccupied);

        Assert.Equal("CD", result.Items[4].Value.AsString());
        Assert.Equal(S7DataType.String, result.Items[4].Value.StringEncoding);
        Assert.Equal(4, result.Items[4].BytesOccupied);

        Assert.Equal("X", result.Items[5].Value.AsString());
        Assert.Equal(S7DataType.S7WString, result.Items[5].Value.StringEncoding);
        Assert.Equal(8, result.Items[5].BytesOccupied);

        Assert.Equal(1, simulator.ReadFrameCount);
        result.EnsureAllSucceeded();
    }

    [Fact]
    public async Task ReadTypedBatchAsync_DecodeFailure_IsolatesToFailedItem()
    {
        var simulator = new BatchPlcSimulator();
        simulator.Memory[0] = 0x00;
        simulator.Memory[1] = 0x07;
        simulator.Memory[300] = 200;
        simulator.Memory[301] = 6;

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7TypedBatchReadResult result = await client.ReadTypedBatchAsync(
        [
            new S7TypedReadBatchItem("DB1.DBB300", S7DataType.S7String, 4),
            new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int)
        ]);

        Assert.False(result.AllSucceeded);
        Assert.False(result.Items[0].Succeeded);
        Assert.Equal(S7ErrorCode.InvalidS7StringHeader, result.Items[0].ErrorCode);
        Assert.False(result.Items[0].Value.IsStringValue);

        Assert.True(result.Items[1].Succeeded);
        Assert.Equal((short)7, result.Items[1].Value.AsInt16());
        Assert.Single(result.Failures);
        Assert.Equal(0, result.Failures[0].Index);
    }

    [Fact]
    public async Task WriteTypedBatchAsync_EncodesValuesAndSingleBitWrite()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7TypedBatchWriteResult result = await client.WriteTypedBatchAsync(
        [
            new S7TypedWriteBatchItem("DB1.DBW0", S7DataType.Int, S7Value.FromInt16(-5)),
            new S7TypedWriteBatchItem("DB1.DBX4.2", S7DataType.Bool, S7Value.FromBool(true))
        ]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(2, result.Items[0].BytesWritten);
        Assert.Equal(1, result.Items[1].BytesWritten);
        Assert.Equal(3, result.BytesWritten);
        Assert.Equal(new byte[] { 0xFF, 0xFB }, simulator.Memory[..2]);
        Assert.Equal(0b0000_0100, simulator.Memory[4]);
        Assert.Equal(4, result.Items[1].Address.StartByte);
        Assert.Equal(2, result.Items[1].Address.BitNumber);
        Assert.Equal(1, simulator.WriteFrameCount);
    }

    [Fact]
    public async Task ReadBatchAsync_ExceedsPdu_Throws142WithoutSendingReadFrame()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        var items = new S7BatchReadItem[18];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new S7BatchReadItem(S7Area.DataBlock, 1, i * 40, 40);
        }

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBatchAsync(items));

        Assert.Equal(S7ErrorCode.BatchPduExceeded, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.InvalidArgument, error.Category);
        Assert.Contains("480", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, simulator.ReadFrameCount);
    }

    [Fact]
    public async Task WriteBatchAsync_ExceedsPdu_Throws142WithoutSendingWriteFrame()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        var items = new S7BatchWriteItem[30];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new S7BatchWriteItem(S7Area.DataBlock, 1, i * 16, new byte[16]);
        }

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBatchAsync(items));

        Assert.Equal(S7ErrorCode.BatchPduExceeded, error.ErrorCode);
        Assert.Equal(0, simulator.WriteFrameCount);
    }

    [Fact]
    public async Task ReadBatchAsync_EmptyList_Throws141WithoutSendingFrame()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBatchAsync([]));

        Assert.Equal(S7ErrorCode.BatchItemsEmpty, error.ErrorCode);
        Assert.Equal(0, simulator.ReadFrameCount);
    }

    [Fact]
    public async Task WriteBatchAsync_TooManyItems_Throws146WithoutSendingFrame()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        var items = new S7BatchWriteItem[256];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new S7BatchWriteItem(S7Area.Memory, 0, i, new byte[1]);
        }

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBatchAsync(items));

        Assert.Equal(S7ErrorCode.BatchItemCountExceeded, error.ErrorCode);
        Assert.Contains("255", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, simulator.WriteFrameCount);
    }

    [Fact]
    public async Task ReadBatchAsync_NullItems_ThrowsArgumentNullException()
    {
        var simulator = new BatchPlcSimulator();

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ReadBatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.WriteBatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ReadTypedBatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.WriteTypedBatchAsync(null!));
    }

    [Fact]
    public async Task ReadBatchAsync_Timeout_ThrowsReadTimeoutAndKeepsSession()
    {
        var simulator = new BatchPlcSimulator { SuppressReadResponses = true };

        await using S7Client client = CreateClient(simulator, readTimeout: TimeSpan.FromSeconds(1));
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBatchAsync([new S7BatchReadItem(S7Area.DataBlock, 1, 0, 1)]));

        Assert.Equal(S7ErrorCode.ReadTimeout, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Timeout, error.Category);
        Assert.Equal(S7OperationStage.Read, error.Stage);
        Assert.False(error.WriteOutcomeUncertain);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task WriteBatchAsync_TimeoutAfterSend_MarksOutcomeUncertain()
    {
        var simulator = new BatchPlcSimulator { SuppressWriteResponses = true };

        await using S7Client client = CreateClient(simulator, writeTimeout: TimeSpan.FromSeconds(1));
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBatchAsync([new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 0x01 })]));

        Assert.Equal(S7ErrorCode.WriteTimeout, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Timeout, error.Category);
        Assert.Equal(S7OperationStage.Write, error.Stage);
        Assert.True(error.WriteOutcomeUncertain);
        Assert.Equal(1, simulator.WriteFrameCount);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task WriteBatchAsync_TransportFault_ThrowsConnectionReset()
    {
        var simulator = new BatchPlcSimulator { ThrowOnWrite = true };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.WriteBatchAsync([new S7BatchWriteItem(S7Area.DataBlock, 1, 0, new byte[] { 0x01 })]));

        Assert.Equal(S7ErrorCode.ConnectionReset, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Connection, error.Category);
        Assert.Equal(S7OperationStage.Write, error.Stage);
    }

    [Fact]
    public async Task ReadBatchAsync_ResponseItemCountMismatch_ThrowsProtocolError()
    {
        var simulator = new BatchPlcSimulator { ResponseItemCountOverride = 2 };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBatchAsync([new S7BatchReadItem(S7Area.DataBlock, 1, 0, 1)]));

        Assert.Equal(S7ErrorCode.InvalidS7Header, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Protocol, error.Category);
        Assert.Contains("条目数不匹配", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTypedBatchAsync_EnsureAllSucceeded_Throws144WithSucceededCount()
    {
        var simulator = new BatchPlcSimulator
        {
            ReadItemProvider = index => index == 0 ? ((byte)0x05, 0) : (ProtocolConstantsSuccess, 16)
        };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        S7TypedBatchReadResult result = await client.ReadTypedBatchAsync(
        [
            new S7TypedReadBatchItem("DB1.DBW0", S7DataType.Int),
            new S7TypedReadBatchItem("DB1.DBW2", S7DataType.Int)
        ]);

        S7ClientException error = Assert.Throws<S7ClientException>(result.EnsureAllSucceeded);

        Assert.Equal(S7ErrorCode.BatchItemFailed, error.ErrorCode);
        Assert.Equal(S7OperationStage.Read, error.Stage);
        Assert.Equal(1, error.SucceededItemCount);
        Assert.Single(error.BatchFailedItems);
        Assert.Equal(0, error.BatchFailedItems[0].Index);
    }

    [Fact]
    public async Task ReadBatchAsync_ConnectionLoss_Returns301_ThenNextBatchSucceeds()
    {
        var simulator = new BatchPlcSimulator { FailFirstReadOnce = true };
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            Port = 102
        };

        await using var client = new S7Client(options, logger: null, transport: simulator);
        await client.ConnectAsync();

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(() =>
            client.ReadBatchAsync(
            [
                new S7BatchReadItem(S7Area.DataBlock, 1, 0, 2),
                new S7BatchReadItem(S7Area.DataBlock, 1, 4, 2)
            ]));

        Assert.Equal(S7ErrorCode.ConnectionReset, error.ErrorCode);

        await WaitUntilAsync(() => client.IsConnected);
        S7BatchReadResult result = await client.ReadBatchAsync(
        [
            new S7BatchReadItem(S7Area.DataBlock, 1, 0, 2),
            new S7BatchReadItem(S7Area.DataBlock, 1, 4, 2)
        ]);

        Assert.True(result.AllSucceeded);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ReadBatchAsync_CallerCancellation_ThrowsOperationCancelled()
    {
        var simulator = new BatchPlcSimulator { ResponseDelay = TimeSpan.FromMilliseconds(800) };

        await using S7Client client = CreateClient(simulator);
        await client.ConnectAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        S7ClientException error = await Assert.ThrowsAsync<S7ClientException>(
            () => client.ReadBatchAsync(
                [new S7BatchReadItem(S7Area.DataBlock, 1, 0, 1)],
                cancellation.Token));

        Assert.Equal(S7ErrorCode.OperationCancelled, error.ErrorCode);
        Assert.Equal(S7ErrorCategory.Cancelled, error.Category);
        Assert.Equal(S7OperationStage.Read, error.Stage);
    }

    private const byte ProtocolConstantsSuccess = 0xFF;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(10);
        }
    }

    private static S7Client CreateClient(
        BatchPlcSimulator simulator,
        TimeSpan? readTimeout = null,
        TimeSpan? writeTimeout = null)
    {
        var options = new S7ConnectionOptions
        {
            IpAddress = "127.0.0.1",
            CpuType = S7CpuType.S71200,
            Rack = 0,
            Slot = 1,
            Port = 102,
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(10),
            WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds(10)
        };

        return new S7Client(options, logger: null, transport: simulator);
    }
}