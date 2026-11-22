using System.Globalization;
using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Parsing;

public static class TypedValueParser
{
    public static bool TryParse(
        S7DataType dataType,
        int capacity,
        string? text,
        bool boolValue,
        out S7Value value,
        out FieldValidationError? error)
    {
        value = default;
        error = null;

        switch (dataType)
        {
            case S7DataType.Bool:
                value = S7Value.FromBool(boolValue);
                return true;

            case S7DataType.Byte:
                if (!TryParseSigned(text, out long byteValue) || byteValue is < byte.MinValue or > byte.MaxValue)
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "Byte 值必须在 0 至 255 之间。", out value, out error);
                }

                value = S7Value.FromByte((byte)byteValue);
                return true;

            case S7DataType.Word:
                if (!TryParseUnsigned(text, out ulong wordValue) || wordValue > ushort.MaxValue)
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "Word 值必须在 0 至 65535 之间。", out value, out error);
                }

                value = S7Value.FromUInt16((ushort)wordValue);
                return true;

            case S7DataType.Int:
                if (!TryParseSigned(text, out long int16Value) || int16Value is < short.MinValue or > short.MaxValue)
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "Int 值必须在 -32768 至 32767 之间。", out value, out error);
                }

                value = S7Value.FromInt16((short)int16Value);
                return true;

            case S7DataType.DWord:
                if (!TryParseUnsigned(text, out ulong dwordValue) || dwordValue > uint.MaxValue)
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "DWord 值必须在 0 至 4294967295 之间。", out value, out error);
                }

                value = S7Value.FromUInt32((uint)dwordValue);
                return true;

            case S7DataType.DInt:
                if (!TryParseSigned(text, out long int32Value) || int32Value is < int.MinValue or > int.MaxValue)
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "DInt 值超出 Int32 范围。", out value, out error);
                }

                value = S7Value.FromInt32((int)int32Value);
                return true;

            case S7DataType.Real:
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float singleValue))
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "Real 值格式非法。", out value, out error);
                }

                value = S7Value.FromSingle(singleValue);
                return true;

            case S7DataType.LReal:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
                {
                    return Fail(S7ErrorCode.ValueOutOfRange, "LReal 值格式非法。", out value, out error);
                }

                value = S7Value.FromDouble(doubleValue);
                return true;

            case S7DataType.String:
            case S7DataType.S7String:
            case S7DataType.S7WString:
                string stringValue = text ?? string.Empty;
                if ((dataType is S7DataType.String or S7DataType.S7String) && stringValue.Any(static c => c > 0x7F))
                {
                    return Fail(S7ErrorCode.StringEncodingInvalid, "String/S7String 仅允许 ASCII 字符。", out value, out error);
                }

                if (stringValue.Length > capacity)
                {
                    return Fail(S7ErrorCode.StringCapacityInvalid, $"内容长度 {stringValue.Length} 超过容量 {capacity}。", out value, out error);
                }

                value = S7Value.FromString(stringValue);
                return true;

            default:
                return Fail(S7ErrorCode.DataTypeNotSupported, "不支持的数据类型。", out value, out error);
        }
    }

    private static bool Fail(
        S7ErrorCode code,
        string message,
        out S7Value value,
        out FieldValidationError? error)
    {
        value = default;
        error = new FieldValidationError("Value", code, message);
        return false;
    }

    private static bool TryParseSigned(string? text, out long value)
    {
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseUnsigned(string? text, out ulong value)
    {
        return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}