using System.Text;

namespace S7.NetCore.Tools.Parsing;

public static class HexByteCodec
{
    public readonly record struct FormattedBytes(string Hex, string Ascii);

    public static bool TryParse(string? text, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "十六进制内容不能为空。";
            return false;
        }

        var values = new List<byte>();
        int index = 0;

        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current) || current is ',' or ';')
            {
                index++;
                continue;
            }

            if (current == '0' && index + 1 < text.Length && text[index + 1] is 'x' or 'X')
            {
                index += 2;
            }

            int start = index;
            int digits = 0;
            while (index < text.Length && IsHex(text[index]))
            {
                digits++;
                index++;
                if (digits > 2)
                {
                    error = $"第 {index} 个字符处字节长度超过两位。";
                    return false;
                }
            }

            if (digits == 0)
            {
                error = $"第 {start + 1} 个字符不是十六进制数字。";
                return false;
            }

            if (digits == 1)
            {
                error = $"第 {start + 1} 个字符处为半字节，必须补齐两位。";
                return false;
            }

            values.Add(Convert.ToByte(text.Substring(start, 2), 16));
        }

        if (values.Count is < 1 or > 65535)
        {
            error = "字节数必须在 1 至 65535 之间。";
            return false;
        }

        bytes = values.ToArray();
        return true;
    }

    public static FormattedBytes Format(ReadOnlySpan<byte> data, int groupSize)
    {
        int group = groupSize is 1 or 2 or 4 or 8 or 16 ? groupSize : 1;
        var hex = new StringBuilder(data.Length * 3);
        var ascii = new StringBuilder(data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                hex.Append(i % group == 0 ? ' ' : ' ');
            }

            hex.Append(data[i].ToString("X2"));
            if (i > 0 && i % group == 0 && i < data.Length)
            {
                hex.Append(' ');
            }

            byte value = data[i];
            ascii.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return new FormattedBytes(hex.ToString().Trim(), ascii.ToString());
    }

    private static bool IsHex(char value)
    {
        return value is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
    }
}