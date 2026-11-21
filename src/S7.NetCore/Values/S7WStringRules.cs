namespace S7.NetCore.Internal;

/// <summary>
/// 字符串容量策略与代理项规则。窄字符与宽字符的容量单位与上限集中在本类。
/// </summary>
internal static class S7WStringRules
{
    /// <summary>S7WString 头部固定长度。</summary>
    internal const int HeaderSize = 4;

    /// <summary>S7WString 最大容量（UTF-16 码元）。</summary>
    internal const int MaxCapacity = 16383;

    /// <summary>String/S7String 最大容量（ASCII 字节）。</summary>
    internal const int MaxNarrowCapacity = 254;

    internal static bool IsWideString(S7DataType dataType)
    {
        return dataType == S7DataType.S7WString;
    }

    internal static void GetCapacityLimits(S7DataType dataType, out int minimum, out int maximum)
    {
        minimum = 1;
        maximum = IsWideString(dataType) ? MaxCapacity : MaxNarrowCapacity;
    }

    internal static string GetCapacityUnit(S7DataType dataType)
    {
        return IsWideString(dataType) ? "UTF-16 码元" : "ASCII 字节";
    }

    internal static int ValidateCapacity(S7DataType dataType, int capacity)
    {
        GetCapacityLimits(dataType, out int minimum, out int maximum);

        if (capacity < minimum || capacity > maximum)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StringCapacityInvalid,
                S7OperationStage.Validate,
                $"数据类型 {dataType} 的容量 {capacity} 非法：允许 {minimum}～{maximum} {GetCapacityUnit(dataType)}。");
        }

        return capacity;
    }

    internal static int GetMaxContentLength(S7DataType dataType, int capacity)
    {
        return dataType == S7DataType.String ? capacity - 1 : capacity;
    }

    internal static void ValidateContentLength(S7DataType dataType, int contentLength, int capacity)
    {
        int maximum = GetMaxContentLength(dataType, capacity);

        if (contentLength < 0 || contentLength > maximum)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StringCapacityInvalid,
                S7OperationStage.Validate,
                $"数据类型 {dataType} 的内容长度 {contentLength} 超出容量允许的最大长度 {maximum} "
                + $"（容量 {capacity} {GetCapacityUnit(dataType)}）。");
        }
    }

    internal static bool ContainsUnpairedSurrogate(string text, out int index)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];

            if (char.IsHighSurrogate(current))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    index = i;
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(current))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}