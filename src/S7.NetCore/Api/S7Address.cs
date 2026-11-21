using S7.NetCore.Internal;

namespace S7.NetCore;

/// <summary>
/// 已解析的地址表达式结果。
/// </summary>
public readonly record struct S7Address
{
    /// <summary>存储区域。</summary>
    public S7Area Area { get; init; }

    /// <summary>数据块号；非数据块区为 0。</summary>
    public int DbNumber { get; init; }

    /// <summary>起始字节地址。</summary>
    public int StartByte { get; init; }

    /// <summary>位号；非位地址为 <see langword="null"/>。</summary>
    public int? BitNumber { get; init; }

    /// <summary>访问宽度。</summary>
    public S7AccessWidth Width { get; init; }

    /// <summary>裁剪后的原始表达式。</summary>
    public string? Expression { get; init; }

    /// <summary>
    /// 解析地址表达式。支持 <c>DB1.DBW0</c>、<c>DB1.DBB0</c>、<c>DB1.DBD0</c>、<c>DB1.DBX0.3</c>、
    /// <c>MW10</c>、<c>Q0.3</c> 等格式；不支持符号寻址与 T/C/Z 区域。
    /// </summary>
    /// <param name="expression">地址表达式。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="S7ClientException">
    /// 表达式非法时抛出，类别为 <see cref="S7ErrorCategory.InvalidArgument"/>，
    /// 错误码为 <see cref="S7ErrorCode.AddressSyntaxInvalid"/>、<see cref="S7ErrorCode.BitNumberOutOfRange"/>、
    /// <see cref="S7ErrorCode.AddressOutOfRange"/> 或 <see cref="S7ErrorCode.AreaNotSupported"/>。
    /// </exception>
    public static S7Address Parse(string expression)
    {
        return S7AddressParser.Parse(expression);
    }

    /// <summary>
    /// 尝试解析地址表达式，不抛出异常。
    /// </summary>
    /// <param name="expression">地址表达式。</param>
    /// <param name="address">解析成功时的地址；失败时为默认值。</param>
    /// <returns>解析成功返回 <see langword="true"/>。</returns>
    public static bool TryParse(string expression, out S7Address address)
    {
        try
        {
            address = Parse(expression);
            return true;
        }
        catch (S7ClientException)
        {
            address = default;
            return false;
        }
    }
}