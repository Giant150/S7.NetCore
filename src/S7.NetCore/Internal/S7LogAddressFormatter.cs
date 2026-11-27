using System.Globalization;

namespace S7.NetCore.Internal;

/// <summary>
/// 日志地址文本格式化：把区域、DB 号与起始字节统一成可检索的地址形态。
/// </summary>
internal static class S7LogAddressFormatter
{
    internal static string Format(S7Area area, int dbNumber, int startByte, int length, S7DataType? dataType)
    {
        string start = startByte.ToString(CultureInfo.InvariantCulture);
        return area switch
        {
            S7Area.DataBlock => $"DB{dbNumber.ToString(CultureInfo.InvariantCulture)}.{DbSuffix(dataType)}{start}",
            S7Area.Memory => $"M{start}.0",
            S7Area.Input => $"I{start}.0",
            S7Area.Output => $"Q{start}.0",
            _ => $"{area}{start}"
        };
    }

    internal static string FormatExpression(string? expression)
    {
        return expression ?? string.Empty;
    }

    private static string DbSuffix(S7DataType? dataType)
    {
        return dataType switch
        {
            S7DataType.Word or S7DataType.Int => "DBW",
            S7DataType.DWord or S7DataType.DInt or S7DataType.Real => "DBD",
            _ => "DBB"
        };
    }
}