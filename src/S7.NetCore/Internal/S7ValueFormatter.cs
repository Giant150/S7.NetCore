using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace S7.NetCore.Internal;

/// <summary>
/// 值文本格式化：字节十六进制、类型化单值、结构体 JSON 与批量逐条列表。
/// 所有方法永不抛出异常，失败时返回格式化降级结果。
/// </summary>
internal static class S7ValueFormatter
{
    internal const int HexMaxBytes = 256;
    internal const int TypedMaxChars = 1024;
    internal const int JsonMaxChars = 4096;
    internal const int BatchMaxItems = 32;

    internal static S7ValueText FormatHex(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.IsEmpty)
            {
                return new S7ValueText { Text = "0 字节", Format = S7ValueFormat.Hex };
            }

            int take = Math.Min(data.Length, HexMaxBytes);
            string hex = Convert.ToHexString(data.Slice(0, take));
            bool truncated = take < data.Length;
            return new S7ValueText
            {
                Text = truncated
                    ? hex + "…(+" + (data.Length - take).ToString(CultureInfo.InvariantCulture) + " 字节)"
                    : hex,
                Format = S7ValueFormat.Hex,
                Truncated = truncated
            };
        }
        catch (Exception)
        {
            return Failed();
        }
    }

    internal static S7ValueText FormatTyped(S7DataType dataType, in S7Value value)
    {
        try
        {
            string text = dataType switch
            {
                S7DataType.Bool => value.AsBoolean() ? "True" : "False",
                S7DataType.Byte => value.GetUnsignedInteger().ToString(CultureInfo.InvariantCulture),
                S7DataType.Word => value.GetUnsignedInteger().ToString(CultureInfo.InvariantCulture),
                S7DataType.Int => value.GetSignedInteger().ToString(CultureInfo.InvariantCulture),
                S7DataType.DWord => value.GetUnsignedInteger().ToString(CultureInfo.InvariantCulture),
                S7DataType.DInt => value.GetSignedInteger().ToString(CultureInfo.InvariantCulture),
                S7DataType.Real => value.IsSingle
                    ? FormatSingle(value.GetSingleValue())
                    : FormatDouble(value.GetDoubleValue()),
                S7DataType.LReal => value.IsDouble
                    ? FormatDouble(value.GetDoubleValue())
                    : FormatSingle(value.GetSingleValue()),
                S7DataType.String or S7DataType.S7String or S7DataType.S7WString =>
                    "\"" + EscapeJson(value.GetStringValue()) + "\"",
                _ => throw new InvalidOperationException($"不支持的数据类型 {dataType}。")
            };

            string label = dataType + "=" + text;
            if (label.Length > TypedMaxChars)
            {
                return new S7ValueText
                {
                    Text = label.Substring(0, TypedMaxChars) + "…",
                    Format = S7ValueFormat.Typed,
                    Truncated = true
                };
            }

            return new S7ValueText { Text = label, Format = S7ValueFormat.Typed };
        }
        catch (Exception)
        {
            return Failed();
        }
    }

    internal static S7ValueText FormatStruct(object boxedValue, StructLayoutSnapshot snapshot)
    {
        try
        {
            if (boxedValue is null || snapshot.Fields.Count == 0)
            {
                return Failed();
            }

            string json = WriteStructJson(boxedValue, snapshot, JsonMaxChars, out int omitted);
            if (omitted == 0 && json.Length <= JsonMaxChars)
            {
                return new S7ValueText { Text = json, Format = S7ValueFormat.Json };
            }

            return new S7ValueText
            {
                Text = json,
                Format = S7ValueFormat.Json,
                Truncated = true
            };
        }
        catch (Exception)
        {
            return Failed();
        }
    }

    internal static S7ValueText FormatBatchItems(ReadOnlySpan<S7BatchValueEntry> entries)
    {
        try
        {
            int count = Math.Min(entries.Length, BatchMaxItems);
            var builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                S7BatchValueEntry entry = entries[i];
                builder.Append('[')
                    .Append(entry.Index.ToString(CultureInfo.InvariantCulture))
                    .Append("] ")
                    .Append(entry.Address)
                    .Append('=')
                    .Append(entry.ErrorCode is { } code
                        ? code.ToString()
                        : entry.ValueText ?? "0 字节");
            }

            bool truncated = entries.Length > count;
            if (truncated)
            {
                builder.Append("…(+")
                    .Append((entries.Length - count).ToString(CultureInfo.InvariantCulture))
                    .Append(" 条未展示)");
            }

            return new S7ValueText
            {
                Text = builder.ToString(),
                Format = S7ValueFormat.Mixed,
                Truncated = truncated,
                ItemCount = count
            };
        }
        catch (Exception)
        {
            return Failed();
        }
    }

    private static string WriteStructJson(
        object instance,
        StructLayoutSnapshot snapshot,
        int maxChars,
        out int omittedFields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        int writtenFields = 0;
        omittedFields = 0;
        foreach (StructFieldDescriptor field in snapshot.Fields)
        {
            if (maxChars != int.MaxValue)
            {
                writer.Flush();
                int currentChars = Encoding.UTF8.GetCharCount(buffer.WrittenSpan);
                int fieldBudget = field.Name.Length + EstimateFieldChars(in field, instance) + 48;
                if (currentChars + fieldBudget > maxChars - 64)
                {
                    omittedFields = snapshot.Fields.Count - writtenFields;
                    break;
                }
            }

            WriteStructField(writer, field, instance);
            writtenFields++;
        }

        if (omittedFields > 0)
        {
            writer.WriteBoolean("_truncated", true);
            writer.WriteNumber("_omittedFields", omittedFields);
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static int EstimateFieldChars(in StructFieldDescriptor field, object instance)
    {
        if (field.DataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString)
        {
            string? text = field.GetValue(instance) as string;
            return text is null ? 4 : (text.Length * 6) + 8;
        }

        return 32;
    }

    private static void WriteStructField(Utf8JsonWriter writer, in StructFieldDescriptor field, object instance)
    {
        object? value = field.GetValue(instance);
        switch (field.DataType)
        {
            case S7DataType.Bool:
                writer.WriteBoolean(field.Name, value is bool booleanValue && booleanValue);
                break;

            case S7DataType.Byte:
                writer.WriteNumber(field.Name, Convert.ToByte(value, CultureInfo.InvariantCulture));
                break;

            case S7DataType.Word:
                writer.WriteNumber(field.Name, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                break;

            case S7DataType.Int:
                writer.WriteNumber(field.Name, Convert.ToInt16(value, CultureInfo.InvariantCulture));
                break;

            case S7DataType.DWord:
                writer.WriteNumber(field.Name, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                break;

            case S7DataType.DInt:
                writer.WriteNumber(field.Name, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                break;

            case S7DataType.Real:
                float single = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsFinite(single))
                {
                    writer.WriteNumber(field.Name, single);
                }
                else
                {
                    writer.WriteString(field.Name, FormatSingle(single));
                }

                break;

            case S7DataType.LReal:
                double @double = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsFinite(@double))
                {
                    writer.WriteNumber(field.Name, @double);
                }
                else
                {
                    writer.WriteString(field.Name, FormatDouble(@double));
                }

                break;

            case S7DataType.String:
            case S7DataType.S7String:
            case S7DataType.S7WString:
                writer.WriteString(field.Name, value as string ?? string.Empty);
                break;

            default:
                throw new InvalidOperationException($"不支持的结构体字段类型 {field.DataType}。");
        }
    }

    private static string FormatSingle(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string EscapeJson(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        foreach (char character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u")
                            .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static S7ValueText Failed()
    {
        return new S7ValueText { Format = S7ValueFormat.None, FormatFailed = true };
    }
}

/// <summary>批量逐条日志的值条目。</summary>
internal readonly record struct S7BatchValueEntry(
    int Index,
    string Address,
    string? ValueText,
    S7ErrorCode? ErrorCode);