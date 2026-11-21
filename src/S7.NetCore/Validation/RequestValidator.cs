namespace S7.NetCore.Internal;

/// <summary>
/// 读写请求校验器。校验仅读取入参，无副作用。
/// </summary>
internal static class RequestValidator
{
    /// <summary>
    /// 校验读取请求。
    /// </summary>
    /// <param name="request">读取请求。</param>
    /// <exception cref="S7ClientException">请求非法时抛出。</exception>
    internal static void ValidateRead(in S7ReadRequest request)
    {
        ValidateArea(request.Area, allowInput: true);
        ValidateAddress(request.Area, request.DbNumber, request.StartByte);

        if (request.Length is < 1 or > ProtocolConstants.MaxTransferLength)
        {
            throw Invalid(S7ErrorCode.InvalidLength, $"Length 必须在 1 至 {ProtocolConstants.MaxTransferLength} 之间，当前值：{request.Length}。");
        }
    }

    /// <summary>
    /// 校验写入请求。
    /// </summary>
    /// <param name="request">写入请求。</param>
    /// <exception cref="S7ClientException">请求非法时抛出。</exception>
    internal static void ValidateWrite(in S7WriteRequest request)
    {
        ValidateArea(request.Area, allowInput: false);
        ValidateAddress(request.Area, request.DbNumber, request.StartByte);

        if (request.Data.IsEmpty)
        {
            throw Invalid(S7ErrorCode.InvalidData, "Data 不得为空，写入长度必须在 1 至 65535 字节之间。");
        }

        if (request.Data.Length > ProtocolConstants.MaxTransferLength)
        {
            throw Invalid(
                S7ErrorCode.InvalidData,
                $"Data 长度必须在 1 至 {ProtocolConstants.MaxTransferLength} 字节之间，当前值：{request.Data.Length}。");
        }
    }

    private static void ValidateArea(S7Area area, bool allowInput)
    {
        if (area is not (S7Area.Input or S7Area.Output or S7Area.Memory or S7Area.DataBlock))
        {
            throw Invalid(S7ErrorCode.AreaNotSupported, $"Area 不支持：{area}。本期仅支持 Input、Output、Memory、DataBlock。");
        }

        if (!allowInput && area == S7Area.Input)
        {
            throw Invalid(S7ErrorCode.AreaNotSupported, "过程输入区（Input）不支持写入。");
        }
    }

    private static void ValidateAddress(S7Area area, int dbNumber, int startByte)
    {
        if (area == S7Area.DataBlock)
        {
            if (dbNumber is < 1 or > 65535)
            {
                throw Invalid(S7ErrorCode.InvalidDbNumber, $"读取或写入数据块区时 DbNumber 必须在 1 至 65535 之间，当前值：{dbNumber}。");
            }
        }
        else if (dbNumber != 0)
        {
            throw Invalid(S7ErrorCode.InvalidDbNumber, $"非数据块区 DbNumber 必须为 0，当前值：{dbNumber}。");
        }

        if (startByte is < 0 or > ProtocolConstants.MaxStartByte)
        {
            throw Invalid(
                S7ErrorCode.InvalidStartByte,
                $"StartByte 必须在 0 至 {ProtocolConstants.MaxStartByte} 之间，当前值：{startByte}。");
        }
    }

    private static S7ClientException Invalid(S7ErrorCode code, string message)
    {
        return new S7ClientException(S7ErrorCategory.InvalidArgument, code, S7OperationStage.Validate, message);
    }
}