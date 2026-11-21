using S7.NetCore;

namespace S7.NetCore.Internal;

/// <summary>
/// 地址表达式解析器。手写单遍扫描，纯本地、无网络依赖。
/// </summary>
internal static class S7AddressParser
{
    private const int MaxExpressionLength = 64;
    private const long NumberClamp = 1_000_000;

    internal static S7Address Parse(string expression)
    {
        if (expression is null)
        {
            throw Syntax(null, "表达式为 null");
        }

        string trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            throw Syntax(trimmed, "表达式为空");
        }

        if (trimmed.Length > MaxExpressionLength)
        {
            throw Syntax(trimmed, $"表达式长度 {trimmed.Length} 超过最大值 {MaxExpressionLength}");
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            char current = trimmed[i];
            if (current > 0x7F)
            {
                throw Syntax(trimmed, $"第 {i} 个字符不是 ASCII");
            }

            if (char.IsWhiteSpace(current))
            {
                throw Syntax(trimmed, $"第 {i} 个字符是内部空白");
            }
        }

        string upper = trimmed.ToUpperInvariant();
        return upper.StartsWith("DB", StringComparison.Ordinal)
            ? ParseDataBlock(upper, trimmed)
            : ParseNonDataBlock(upper, trimmed);
    }

    private static S7Address ParseDataBlock(string upper, string expression)
    {
        int index = 2;
        if (index >= upper.Length || !IsDigit(upper[index]))
        {
            throw Syntax(expression, "DB 后必须紧跟 1～65535 的 DB 号；不支持符号寻址（如 DB.DBW0）");
        }

        ReadNumber(upper, ref index, out long dbNumber);
        if (index >= upper.Length || upper[index] != '.')
        {
            throw Syntax(expression, "DB 号后必须为 '.' 与 DBB/DBW/DBD/DBX 后缀");
        }

        index++;
        if (index + 2 >= upper.Length || upper[index] != 'D' || upper[index + 1] != 'B')
        {
            throw Syntax(expression, "DB 后缀必须是 DBB/DBW/DBD/DBX");
        }

        S7AccessWidth width = upper[index + 2] switch
        {
            'B' => S7AccessWidth.Byte,
            'W' => S7AccessWidth.Word,
            'D' => S7AccessWidth.DWord,
            'X' => S7AccessWidth.Bit,
            _ => throw Syntax(expression, "DB 后缀必须是 DBB/DBW/DBD/DBX")
        };
        index += 3;

        if (index >= upper.Length || !IsDigit(upper[index]))
        {
            throw Syntax(expression, "DB 偏移缺失，必须是 0～65535 的十进制数");
        }

        ReadNumber(upper, ref index, out long offset);
        int? bitNumber = null;

        if (width == S7AccessWidth.Bit)
        {
            if (index >= upper.Length)
            {
                throw BitNumberInvalid(expression, "DBX 必须带位号，例如 DB1.DBX0.3");
            }

            if (upper[index] != '.')
            {
                throw Syntax(expression, "DBX 偏移后必须为 '.位号'");
            }

            index++;
            if (index >= upper.Length || !IsDigit(upper[index]))
            {
                throw BitNumberInvalid(expression, "DBX 位号缺失");
            }

            ReadNumber(upper, ref index, out long bit);
            if (bit > 7)
            {
                throw BitNumberInvalid(expression, $"位号 {bit} 超出 0～7");
            }

            bitNumber = (int)bit;
        }
        else if (index < upper.Length)
        {
            throw upper[index] == '.'
                ? BitNumberInvalid(expression, $"{width} 后缀不允许携带位号")
                : Syntax(expression, $"偏移后存在非法字符 '{upper[index]}'");
        }

        if (index != upper.Length)
        {
            throw Syntax(expression, $"表达式在第 {index} 个字符后存在未解析内容");
        }

        if (dbNumber is < 1 or > 65535)
        {
            throw OutOfRange(expression, $"DB 号 {dbNumber} 超出 1～65535");
        }

        if (offset is < 0 or > 65535)
        {
            throw OutOfRange(expression, $"偏移 {offset} 超出 0～65535");
        }

        return new S7Address
        {
            Area = S7Area.DataBlock,
            DbNumber = (int)dbNumber,
            StartByte = (int)offset,
            BitNumber = bitNumber,
            Width = width,
            Expression = expression
        };
    }

    private static S7Address ParseNonDataBlock(string upper, string expression)
    {
        S7Area area = upper[0] switch
        {
            'I' or 'E' => S7Area.Input,
            'Q' or 'A' or 'O' => S7Area.Output,
            'M' => S7Area.Memory,
            'T' or 'C' or 'Z' => throw AreaNotSupported(expression),
            _ => throw Syntax(expression, $"不支持的前缀 '{upper[0]}'；支持 I/E、Q/A/O、M 与 DB")
        };

        int index = 1;
        S7AccessWidth width = S7AccessWidth.Bit;
        bool hasWidth = false;

        if (index < upper.Length)
        {
            switch (upper[index])
            {
                case 'B':
                    width = S7AccessWidth.Byte;
                    hasWidth = true;
                    index++;
                    break;
                case 'W':
                    width = S7AccessWidth.Word;
                    hasWidth = true;
                    index++;
                    break;
                case 'D':
                    width = S7AccessWidth.DWord;
                    hasWidth = true;
                    index++;
                    break;
            }
        }

        if (index >= upper.Length || !IsDigit(upper[index]))
        {
            throw Syntax(expression, "不支持符号寻址；偏移必须是 0～65535 的十进制数，位地址形如 Q0.3");
        }

        ReadNumber(upper, ref index, out long offset);
        int? bitNumber = null;

        if (hasWidth)
        {
            if (index < upper.Length)
            {
                throw upper[index] == '.'
                    ? BitNumberInvalid(expression, $"{width} 后缀不允许携带位号")
                    : Syntax(expression, $"偏移后存在非法字符 '{upper[index]}'");
            }
        }
        else
        {
            if (index >= upper.Length)
            {
                throw BitNumberInvalid(expression, "位地址必须带位号，例如 Q0.3");
            }

            if (upper[index] != '.')
            {
                throw Syntax(expression, "位地址偏移后必须为 '.位号'");
            }

            index++;
            if (index >= upper.Length || !IsDigit(upper[index]))
            {
                throw BitNumberInvalid(expression, "位号缺失");
            }

            ReadNumber(upper, ref index, out long bit);
            if (bit > 7)
            {
                throw BitNumberInvalid(expression, $"位号 {bit} 超出 0～7");
            }

            bitNumber = (int)bit;
        }

        if (index != upper.Length)
        {
            throw Syntax(expression, $"表达式在第 {index} 个字符后存在未解析内容");
        }

        if (offset is < 0 or > 65535)
        {
            throw OutOfRange(expression, $"偏移 {offset} 超出 0～65535");
        }

        return new S7Address
        {
            Area = area,
            DbNumber = 0,
            StartByte = (int)offset,
            BitNumber = bitNumber,
            Width = width,
            Expression = expression
        };
    }

    private static void ReadNumber(string text, ref int index, out long value)
    {
        value = 0;
        while (index < text.Length && IsDigit(text[index]))
        {
            if (value < NumberClamp)
            {
                value = value * 10 + (text[index] - '0');
                if (value > NumberClamp)
                {
                    value = NumberClamp;
                }
            }

            index++;
        }
    }

    private static bool IsDigit(char value)
    {
        return value is >= '0' and <= '9';
    }

    private static S7ClientException Syntax(string? expression, string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.AddressSyntaxInvalid,
            S7OperationStage.Validate,
            $"地址表达式 '{expression ?? "<null>"}' 非法：{reason}。支持 DB<n>.DBB/DBW/DBD/DBX、I/E、Q/A/O、M 前缀，不支持符号寻址。");
    }

    private static S7ClientException BitNumberInvalid(string expression, string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.BitNumberOutOfRange,
            S7OperationStage.Validate,
            $"地址表达式 '{expression}' 位号非法：{reason}。位号必须为 0～7，且仅 X/位形式可携带位号。");
    }

    private static S7ClientException OutOfRange(string expression, string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.AddressOutOfRange,
            S7OperationStage.Validate,
            $"地址表达式 '{expression}' 超出范围：{reason}。DB 号 1～65535，偏移 0～65535。");
    }

    private static S7ClientException AreaNotSupported(string expression)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.AreaNotSupported,
            S7OperationStage.Validate,
            $"地址表达式 '{expression}' 使用了不支持的区域前缀。本期仅支持 I/E、Q/A/O、M 与 DB。");
    }
}