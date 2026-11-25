using System.Diagnostics;
using S7.NetCore.Internal;

namespace S7.NetCore;

/// <summary>
/// 结构体读写门面（partial 增量）。字节与单值类型化 API 保持不变。
/// </summary>
public sealed partial class S7Client
{
    /// <summary>
    /// 从 DB 字节地址读取扁平结构体。字段按声明顺序连续映射，总长度不含运行时对齐填充；
    /// 读取整段字节后一次性解码，任一字段失败不返回部分结果。
    /// </summary>
    /// <typeparam name="T">结构体类型；支持 bool、byte、short、ushort、int、uint、float、double 与带容量标注的 string 字段。</typeparam>
    /// <param name="address">仅支持 <c>DBn.DBBm</c> 字节地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解码后的结构体、总占用长度与解析地址。</returns>
    /// <exception cref="S7ClientException">
    /// 布局非法（132/136/139）、地址非法（130/140/137）、PLC 返回错误、解码失败（404/406/407/408/409）、
    /// 超时、取消或会话重连中（204）。
    /// </exception>
    public async Task<S7StructReadResult<T>> ReadStructAsync<T>(
        string address,
        CancellationToken cancellationToken = default)
        where T : struct
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        StructLayoutSnapshot snapshot;
        S7Address parsed;

        try
        {
            snapshot = StructLayoutResolver.Resolve<T>();
            parsed = S7Address.Parse(address);
            StructAddressValidator.Validate(in parsed, snapshot.TotalLength);
        }
        catch (S7ClientException exception)
        {
            LogStruct(
                "ReadStruct",
                exception.Stage,
                address,
                typeof(T),
                totalLength: null,
                fieldCount: null,
                succeeded: false,
                startTimestamp: startTimestamp,
                errorCode: exception.ErrorCode,
                exception: exception);
            throw;
        }

        try
        {
            ReadOnlyMemory<byte> data = await ReadBytesAsync(
                    new S7ReadRequest(parsed.Area, parsed.DbNumber, parsed.StartByte, snapshot.TotalLength),
                    cancellationToken)
                .ConfigureAwait(false);
            T value = StructDecoder.Decode<T>(data.Span, snapshot);

            LogStruct(
                "ReadStruct",
                S7OperationStage.Read,
                address,
                typeof(T),
                snapshot.TotalLength,
                snapshot.FieldCount,
                succeeded: true,
                startTimestamp: startTimestamp,
                parsed: parsed);

            return new S7StructReadResult<T>(
                value,
                snapshot.TotalLength,
                parsed);
        }
        catch (S7ClientException exception)
        {
            LogStruct(
                "ReadStruct",
                exception.Stage,
                address,
                typeof(T),
                snapshot.TotalLength,
                snapshot.FieldCount,
                succeeded: false,
                startTimestamp: startTimestamp,
                errorCode: exception.ErrorCode,
                exception: exception,
                parsed: parsed);
            throw exception.WithContext(StructContext(typeof(T), parsed, snapshot.TotalLength));
        }
    }

    /// <summary>
    /// 把扁平结构体整段写入 DB 字节地址。成功返回实际写入字节数（等于结构体总占用长度）。
    /// </summary>
    /// <typeparam name="T">结构体类型；支持 bool、byte、short、ushort、int、uint、float、double 与带容量标注的 string 字段。</typeparam>
    /// <param name="address">仅支持 <c>DBn.DBBm</c> 字节地址。</param>
    /// <param name="value">待写入的结构体值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际写入的字节数，恒等于结构体总占用长度。</returns>
    /// <exception cref="S7ClientException">
    /// 布局非法（132/136/139）、地址非法（130/140/137）、字段值非法（134/135/136/138）、
    /// PLC 返回错误、超时、取消或会话重连中（204）。
    /// </exception>
    public async Task<int> WriteStructAsync<T>(
        string address,
        T value,
        CancellationToken cancellationToken = default)
        where T : struct
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        StructLayoutSnapshot snapshot;
        S7Address parsed;

        try
        {
            snapshot = StructLayoutResolver.Resolve<T>();
            parsed = S7Address.Parse(address);
            StructAddressValidator.Validate(in parsed, snapshot.TotalLength);
        }
        catch (S7ClientException exception)
        {
            LogStruct(
                "WriteStruct",
                exception.Stage,
                address,
                typeof(T),
                totalLength: null,
                fieldCount: null,
                succeeded: false,
                startTimestamp: startTimestamp,
                errorCode: exception.ErrorCode,
                exception: exception);
            throw;
        }

        byte[] encoded;
        try
        {
            encoded = StructEncoder.Encode(value, snapshot);
        }
        catch (S7ClientException exception)
        {
            LogStruct(
                "WriteStruct",
                exception.Stage,
                address,
                typeof(T),
                snapshot.TotalLength,
                snapshot.FieldCount,
                succeeded: false,
                startTimestamp: startTimestamp,
                errorCode: exception.ErrorCode,
                exception: exception,
                parsed: parsed);
            throw exception.WithContext(StructContext(typeof(T), parsed, snapshot.TotalLength));
        }

        try
        {
            int written = await WriteBytesAsync(
                    new S7WriteRequest(parsed.Area, parsed.DbNumber, parsed.StartByte, encoded),
                    cancellationToken)
                .ConfigureAwait(false);

            LogStruct(
                "WriteStruct",
                S7OperationStage.Write,
                address,
                typeof(T),
                snapshot.TotalLength,
                snapshot.FieldCount,
                succeeded: true,
                startTimestamp: startTimestamp,
                parsed: parsed);

            return written;
        }
        catch (S7ClientException exception)
        {
            LogStruct(
                "WriteStruct",
                exception.Stage,
                address,
                typeof(T),
                snapshot.TotalLength,
                snapshot.FieldCount,
                succeeded: false,
                startTimestamp: startTimestamp,
                errorCode: exception.ErrorCode,
                exception: exception,
                parsed: parsed);
            throw exception.WithContext(StructContext(typeof(T), parsed, snapshot.TotalLength));
        }
    }

    private void LogStruct(
        string operation,
        S7OperationStage stage,
        string? address,
        Type structType,
        int? totalLength,
        int? fieldCount,
        bool succeeded,
        long startTimestamp,
        S7ErrorCode? errorCode = null,
        Exception? exception = null,
        S7Address? parsed = null)
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
            Area = parsed?.Area,
            DbNumber = parsed?.DbNumber,
            StartByte = parsed?.StartByte,
            Length = totalLength,
            Elapsed = Stopwatch.GetElapsedTime(startTimestamp),
            Succeeded = succeeded,
            ErrorCode = errorCode,
            AddressExpression = address,
            StructTypeName = structType.Name,
            MappedFieldCount = fieldCount
        };

        _logger.Log(level, in entry, exception);
    }

    private static string StructContext(Type structType, in S7Address address, int totalLength)
    {
        return $"结构体 '{structType.Name}'，地址表达式 '{address.Expression}'，总占用 {totalLength} 字节。";
    }
}