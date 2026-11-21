namespace S7.NetCore.Internal;

/// <summary>
/// 传输层内部异常。由会话层统一归一化为 <see cref="S7ClientException"/>。
/// </summary>
internal sealed class TransportException : Exception
{
    /// <summary>
    /// 初始化传输异常。
    /// </summary>
    /// <param name="code">关联错误码。</param>
    /// <param name="message">错误消息。</param>
    /// <param name="innerException">内部异常。</param>
    internal TransportException(S7ErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>关联错误码。</summary>
    internal S7ErrorCode Code { get; }
}