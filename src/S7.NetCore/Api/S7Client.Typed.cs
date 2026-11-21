using System.Diagnostics;
using S7.NetCore.Internal;

namespace S7.NetCore;

/// <summary>
/// 类型化读写门面（partial 增量）。字节 API 保持冻结。
/// </summary>
public sealed partial class S7Client
{
    /// <summary>
    /// 按地址表达式读取带类型的值。先本地解析与校验，再复用字节读取（自动分包），最后按大端序解码。
    /// S7WString 使用 4 字节头与严格 UTF-16BE 解码，头部容量允许小于或等于请求容量。
    /// </summary>
    /// <param name="request">类型化读取请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解码后的值、占用字节数与解析后的地址。</returns>
    /// <exception cref="S7ClientException">
    /// 地址或类型非法（<see cref="S7ErrorCategory.InvalidArgument"/>）、PLC 返回错误、
    /// 协议数据非法（404/406/407/408/409）、超时或取消。失败时不返回任何部分值。
    /// </exception>
    public async Task<S7TypedReadResult> ReadValueAsync(
        S7TypedReadRequest request,
        CancellationToken cancellationToken = default)
    {
        S7Address address = S7Address.Parse(request.Address);
        long startTimestamp = Stopwatch.GetTimestamp();
        int bytesOccupied;

        try
        {
            bytesOccupied = TypedRequestValidator.ValidateRead(in request, in address);
        }
        catch (S7ClientException exception)
        {
            LogTyped(
                "Read",
                S7OperationStage.Validate,
                address,
                request.DataType,
                EstimateOccupiedBytes(request.DataType, request.Capacity),
                succeeded: false,
                startTimestamp,
                exception.ErrorCode,
                exception.PlcReturnCode,
                exception,
                CapacityForLog(request.DataType, request.Capacity));
            throw;
        }

        try
        {
            if (request.DataType == S7DataType.Bool)
            {
                ReadOnlyMemory<byte> buffer = await ReadBytesAsync(
                    new S7ReadRequest(address.Area, address.DbNumber, address.StartByte, 1),
                    cancellationToken).ConfigureAwait(false);

                bool bit = (buffer.Span[0] & (1 << address.BitNumber!.Value)) != 0;
                S7Value boolValue = S7Value.FromBool(bit);
                LogTyped(
                    "Read",
                    S7OperationStage.Read,
                    address,
                    request.DataType,
                    bytesOccupied: 1,
                    succeeded: true,
                    startTimestamp,
                    capacity: CapacityForLog(request.DataType, request.Capacity));

                return new S7TypedReadResult(boolValue, 1, address);
            }

            ReadOnlyMemory<byte> data = await ReadBytesAsync(
                new S7ReadRequest(address.Area, address.DbNumber, address.StartByte, bytesOccupied),
                cancellationToken).ConfigureAwait(false);

            S7Value decoded = S7ValueCodec.Decode(request.DataType, data.Span, request.Capacity);
            LogTyped(
                "Read",
                S7OperationStage.Read,
                address,
                request.DataType,
                bytesOccupied,
                succeeded: true,
                startTimestamp,
                capacity: CapacityForLog(request.DataType, request.Capacity));

            return new S7TypedReadResult(decoded, bytesOccupied, address);
        }
        catch (S7ClientException exception)
        {
            LogTyped(
                "Read",
                S7OperationStage.Read,
                address,
                request.DataType,
                bytesOccupied,
                succeeded: false,
                startTimestamp,
                exception.ErrorCode,
                exception.PlcReturnCode,
                exception,
                CapacityForLog(request.DataType, request.Capacity));

            throw exception.WithContext(BuildContext(address, request.DataType, request.Capacity, bytesOccupied));
        }
    }

    /// <summary>
    /// 按地址表达式写入带类型的值。先本地解析、校验与编码，再复用字节写入（自动分包）；位写使用单请求位写。
    /// S7WString 使用 4 字节头与 UTF-16BE 编码，内容按 UTF-16 码元计。
    /// </summary>
    /// <param name="request">类型化写入请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际写入的字节数，恒等于该类型占用字节数；Bool 为 1，S7WString 为 4 + 2 × Capacity。</returns>
    /// <exception cref="S7ClientException">
    /// 地址、类型、值或容量非法（<see cref="S7ErrorCategory.InvalidArgument"/>）、Input 区写入被拒绝、
    /// PLC 返回错误、超时或取消。异常中的写入进度字段与字节写入语义一致。
    /// </exception>
    public async Task<int> WriteValueAsync(
        S7TypedWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        S7Address address = S7Address.Parse(request.Address);
        long startTimestamp = Stopwatch.GetTimestamp();
        int bytesOccupied;

        try
        {
            bytesOccupied = TypedRequestValidator.ValidateWrite(in request, in address);
        }
        catch (S7ClientException exception)
        {
            LogTyped(
                "Write",
                S7OperationStage.Validate,
                address,
                request.DataType,
                EstimateOccupiedBytes(request.DataType, request.Capacity),
                succeeded: false,
                startTimestamp,
                exception.ErrorCode,
                exception.PlcReturnCode,
                exception,
                CapacityForLog(request.DataType, request.Capacity));
            throw;
        }

        try
        {
            int written;

            if (request.DataType == S7DataType.Bool)
            {
                bool value = request.Value.AsBoolean();
                written = await _session.WriteBitAsync(
                    new S7WriteRequest(address.Area, address.DbNumber, address.StartByte, ReadOnlyMemory<byte>.Empty),
                    address.BitNumber!.Value,
                    value,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                byte[] encoded = S7ValueCodec.Encode(request.DataType, request.Value, request.Capacity);
                written = await WriteBytesAsync(
                    new S7WriteRequest(address.Area, address.DbNumber, address.StartByte, encoded),
                    cancellationToken).ConfigureAwait(false);
            }

            LogTyped(
                "Write",
                S7OperationStage.Write,
                address,
                request.DataType,
                bytesOccupied,
                succeeded: true,
                startTimestamp,
                capacity: CapacityForLog(request.DataType, request.Capacity));

            return written;
        }
        catch (S7ClientException exception)
        {
            LogTyped(
                "Write",
                S7OperationStage.Write,
                address,
                request.DataType,
                bytesOccupied,
                succeeded: false,
                startTimestamp,
                exception.ErrorCode,
                exception.PlcReturnCode,
                exception,
                CapacityForLog(request.DataType, request.Capacity));

            throw exception.WithContext(BuildContext(address, request.DataType, request.Capacity, bytesOccupied));
        }
    }

    private static int? CapacityForLog(S7DataType dataType, int capacity)
    {
        return dataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString
            ? capacity
            : null;
    }

    private static int EstimateOccupiedBytes(S7DataType dataType, int capacity)
    {
        return dataType switch
        {
            S7DataType.Bool or S7DataType.Byte => 1,
            S7DataType.Word or S7DataType.Int => 2,
            S7DataType.DWord or S7DataType.DInt or S7DataType.Real => 4,
            S7DataType.LReal => 8,
            S7DataType.String => Math.Max(capacity, 1),
            S7DataType.S7String => Math.Max(capacity, 1) + 2,
            S7DataType.S7WString => S7WStringRules.HeaderSize + (Math.Max(capacity, 0) * 2),
            _ => 0
        };
    }

    private static string BuildContext(
        in S7Address address,
        S7DataType dataType,
        int capacity,
        int bytesOccupied)
    {
        string capacityText = dataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString
            ? $"容量 {capacity}（{S7WStringRules.GetCapacityUnit(dataType)}），"
            : string.Empty;

        return $"地址表达式 '{address.Expression}'，数据类型 {dataType}，{capacityText}占用 {bytesOccupied} 字节。";
    }

    private void LogTyped(
        string operation,
        S7OperationStage stage,
        in S7Address address,
        S7DataType dataType,
        int bytesOccupied,
        bool succeeded,
        long startTimestamp,
        S7ErrorCode? errorCode = null,
        byte? plcReturnCode = null,
        Exception? exception = null,
        int? capacity = null)
    {
        S7LogLevel level = succeeded ? S7LogLevel.Information : S7LogLevel.Warning;
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var entry = new S7LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Stage = stage,
            Area = address.Area,
            DbNumber = address.DbNumber,
            StartByte = address.StartByte,
            Length = bytesOccupied,
            Elapsed = Stopwatch.GetElapsedTime(startTimestamp),
            Succeeded = succeeded,
            ErrorCode = errorCode,
            PlcReturnCode = plcReturnCode,
            AddressExpression = address.Expression,
            DataType = dataType,
            BytesOccupied = bytesOccupied,
            Capacity = capacity
        };

        _logger.Log(level, in entry, exception);
    }
}