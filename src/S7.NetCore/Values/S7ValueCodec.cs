using System.Buffers.Binary;
using System.Text;
using S7.NetCore;

namespace S7.NetCore.Internal;

/// <summary>
/// 值编解码：PLC 大端字节与 C# 值之间的双向转换。纯本地、无网络依赖。
/// </summary>
internal static class S7ValueCodec
{
    internal static byte[] Encode(S7DataType dataType, in S7Value value, int capacity)
    {
        int length = S7TypeRules.GetOccupiedBytes(dataType, capacity);
        var data = new byte[length];

        switch (dataType)
        {
            case S7DataType.Bool:
                data[0] = value.AsBoolean() ? (byte)1 : (byte)0;
                break;

            case S7DataType.Byte:
                data[0] = (byte)value.GetUnsignedInteger();
                break;

            case S7DataType.Word:
                BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)value.GetUnsignedInteger());
                break;

            case S7DataType.Int:
                BinaryPrimitives.WriteUInt16BigEndian(data, unchecked((ushort)value.GetSignedInteger()));
                break;

            case S7DataType.DWord:
                BinaryPrimitives.WriteUInt32BigEndian(data, (uint)value.GetUnsignedInteger());
                break;

            case S7DataType.DInt:
                BinaryPrimitives.WriteUInt32BigEndian(data, unchecked((uint)value.GetSignedInteger()));
                break;

            case S7DataType.Real:
                BinaryPrimitives.WriteUInt32BigEndian(data, BitConverter.SingleToUInt32Bits(value.GetSingleValue()));
                break;

            case S7DataType.LReal:
                double doubleValue = value.IsDouble ? value.GetDoubleValue() : value.GetSingleValue();
                BinaryPrimitives.WriteUInt64BigEndian(data, unchecked((ulong)BitConverter.DoubleToInt64Bits(doubleValue)));
                break;

            case S7DataType.String:
                EncodeCString(value.GetStringValue(), data, capacity);
                break;

            case S7DataType.S7String:
                EncodeS7String(value.GetStringValue(), data, capacity);
                break;

            case S7DataType.S7WString:
                EncodeS7WString(value.GetStringValue(), data, capacity);
                break;

            default:
                throw new S7ClientException(
                    S7ErrorCategory.InvalidArgument,
                    S7ErrorCode.DataTypeNotSupported,
                    S7OperationStage.Validate,
                    $"数据类型 {(int)dataType} 不受支持。");
        }

        return data;
    }

    internal static S7Value Decode(S7DataType dataType, ReadOnlySpan<byte> data, int capacity)
    {
        switch (dataType)
        {
            case S7DataType.Bool:
                EnsureLength(data, 1, dataType);
                return S7Value.FromBool((data[0] & 0x01) != 0);

            case S7DataType.Byte:
                EnsureLength(data, 1, dataType);
                return S7Value.FromByte(data[0]);

            case S7DataType.Word:
                EnsureLength(data, 2, dataType);
                return S7Value.FromUInt16(BinaryPrimitives.ReadUInt16BigEndian(data));

            case S7DataType.Int:
                EnsureLength(data, 2, dataType);
                return S7Value.FromInt16(unchecked((short)BinaryPrimitives.ReadUInt16BigEndian(data)));

            case S7DataType.DWord:
                EnsureLength(data, 4, dataType);
                return S7Value.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(data));

            case S7DataType.DInt:
                EnsureLength(data, 4, dataType);
                return S7Value.FromInt32(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(data)));

            case S7DataType.Real:
                EnsureLength(data, 4, dataType);
                return S7Value.FromSingle(BitConverter.UInt32BitsToSingle(
                    BinaryPrimitives.ReadUInt32BigEndian(data)));

            case S7DataType.LReal:
                EnsureLength(data, 8, dataType);
                return S7Value.FromDouble(BitConverter.Int64BitsToDouble(
                    unchecked((long)BinaryPrimitives.ReadUInt64BigEndian(data))));

            case S7DataType.String:
                return DecodeCString(data, capacity);

            case S7DataType.S7String:
                return DecodeS7String(data, capacity);

            case S7DataType.S7WString:
                return DecodeS7WString(data, capacity);

            default:
                throw new S7ClientException(
                    S7ErrorCategory.InvalidArgument,
                    S7ErrorCode.DataTypeNotSupported,
                    S7OperationStage.Validate,
                    $"数据类型 {(int)dataType} 不受支持。");
        }
    }

    private static void EncodeCString(string text, byte[] data, int capacity)
    {
        if (text.Length + 1 > capacity)
        {
            throw CapacityInvalid($"字符串内容长度 {text.Length} 加终止符超过容量 {capacity}");
        }

        EnsureAsciiForWrite(text);

        for (int i = 0; i < text.Length; i++)
        {
            data[i] = (byte)text[i];
        }

        data[text.Length] = 0x00;
    }

    private static void EncodeS7String(string text, byte[] data, int capacity)
    {
        if (text.Length > capacity)
        {
            throw CapacityInvalid($"字符串内容长度 {text.Length} 超过容量 {capacity}");
        }

        EnsureAsciiForWrite(text);

        data[0] = (byte)capacity;
        data[1] = (byte)text.Length;

        for (int i = 0; i < text.Length; i++)
        {
            data[2 + i] = (byte)text[i];
        }
    }

    private static void EncodeS7WString(string text, byte[] data, int capacity)
    {
        S7WStringRules.ValidateCapacity(S7DataType.S7WString, capacity);

        if (text.Length > capacity)
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StringCapacityInvalid,
                S7OperationStage.Validate,
                $"S7WString 内容长度 {text.Length} 码元超过容量 {capacity} 码元。");
        }

        if (S7WStringRules.ContainsUnpairedSurrogate(text, out int index))
        {
            throw new S7ClientException(
                S7ErrorCategory.InvalidArgument,
                S7ErrorCode.StringEncodingInvalid,
                S7OperationStage.Validate,
                $"S7WString 包含孤立代理项，失败码元索引 {index}。");
        }

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), (ushort)capacity);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            data[4 + (i * 2)] = (byte)(value >> 8);
            data[5 + (i * 2)] = (byte)value;
        }
    }

    private static void EnsureAsciiForWrite(string text)
    {
        foreach (char character in text)
        {
            if (character > 0x7F)
            {
                throw new S7ClientException(
                    S7ErrorCategory.InvalidArgument,
                    S7ErrorCode.StringEncodingInvalid,
                    S7OperationStage.Validate,
                    "字符串包含非 ASCII 字符，编码固定为 ASCII。");
            }
        }
    }

    private static S7ClientException CapacityInvalid(string message)
    {
        return new S7ClientException(
            S7ErrorCategory.InvalidArgument,
            S7ErrorCode.StringCapacityInvalid,
            S7OperationStage.Validate,
            message);
    }

    private static S7Value DecodeCString(ReadOnlySpan<byte> data, int capacity)
    {
        EnsureLength(data, capacity, S7DataType.String);
        ReadOnlySpan<byte> content = data[..capacity];
        int terminator = content.IndexOf((byte)0);
        int length = terminator < 0 ? capacity : terminator;
        EnsureAscii(content[..length]);

        return S7Value.FromString(Encoding.ASCII.GetString(content[..length]));
    }

    private static S7Value DecodeS7String(ReadOnlySpan<byte> data, int capacity)
    {
        if (data.Length < 2)
        {
            throw InvalidHeader("S7 String 数据不足 2 字节头部。");
        }

        int headerCapacity = data[0];
        int length = data[1];

        if (headerCapacity > 254 || length > headerCapacity || length > capacity)
        {
            throw InvalidHeader(
                $"S7 String 头部非法：容量 {headerCapacity}，实际长度 {length}，请求容量 {capacity}。");
        }

        EnsureLength(data, 2 + length, S7DataType.S7String);
        ReadOnlySpan<byte> content = data.Slice(2, length);
        EnsureAscii(content);

        return S7Value.FromString(Encoding.ASCII.GetString(content));
    }

    private static S7Value DecodeS7WString(ReadOnlySpan<byte> data, int capacity)
    {
        S7WStringRules.ValidateCapacity(S7DataType.S7WString, capacity);
        int occupied = S7WStringRules.HeaderSize + (capacity * 2);

        if (data.Length < occupied)
        {
            throw new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"S7WString 整段长度不足：请求容量 {capacity} 码元，期望至少 {occupied} 字节，实际 {data.Length} 字节。");
        }

        if (data.Length < S7WStringRules.HeaderSize)
        {
            throw InvalidWStringHeader(capacity, 0, 0, "数据不足 4 字节头部");
        }

        int headerCapacity = BinaryPrimitives.ReadUInt16BigEndian(data[..2]);
        int declaredLength = BinaryPrimitives.ReadUInt16BigEndian(data[2..4]);

        if (headerCapacity < 1 || headerCapacity > S7WStringRules.MaxCapacity)
        {
            throw InvalidWStringHeader(
                capacity,
                headerCapacity,
                declaredLength,
                $"头部容量不在 1～{S7WStringRules.MaxCapacity} 码元");
        }

        if (headerCapacity > capacity)
        {
            throw InvalidWStringHeader(
                capacity,
                headerCapacity,
                declaredLength,
                $"头部容量大于请求容量 {capacity} 码元");
        }

        if (declaredLength > headerCapacity)
        {
            throw InvalidWStringHeader(
                capacity,
                headerCapacity,
                declaredLength,
                "声明长度大于头部容量");
        }

        if (declaredLength * 2 > data.Length - S7WStringRules.HeaderSize)
        {
            throw InvalidWStringData(capacity, declaredLength, 0, "字符区字节不足");
        }

        if (declaredLength == 0)
        {
            return S7Value.FromString(string.Empty);
        }

        char[] chars = new char[declaredLength];

        for (int i = 0; i < declaredLength; i++)
        {
            int offset = S7WStringRules.HeaderSize + (i * 2);
            char current = (char)BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

            if (char.IsHighSurrogate(current))
            {
                if (i + 1 >= declaredLength)
                {
                    throw InvalidWStringData(capacity, declaredLength, i, "末尾存在不完整的高位代理项");
                }

                int lowOffset = S7WStringRules.HeaderSize + ((i + 1) * 2);
                char low = (char)BinaryPrimitives.ReadUInt16BigEndian(data[lowOffset..]);

                if (!char.IsLowSurrogate(low))
                {
                    throw InvalidWStringData(capacity, declaredLength, i, "高位代理项后未紧跟低位代理项");
                }

                chars[i] = current;
                chars[i + 1] = low;
                i++;
            }
            else if (char.IsLowSurrogate(current))
            {
                throw InvalidWStringData(capacity, declaredLength, i, "存在孤立低位代理项");
            }
            else
            {
                chars[i] = current;
            }
        }

        return S7Value.FromString(new string(chars));
    }

    private static S7ClientException InvalidWStringHeader(
        int capacity,
        int headerCapacity,
        int declaredLength,
        string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.Protocol,
            S7ErrorCode.InvalidS7WStringHeader,
            S7OperationStage.Read,
            $"S7WString 头部非法：请求容量 {capacity} 码元，头部容量 {headerCapacity} 码元，"
            + $"声明长度 {declaredLength} 码元，{reason}。");
    }

    private static S7ClientException InvalidWStringData(
        int capacity,
        int declaredLength,
        int index,
        string reason)
    {
        return new S7ClientException(
            S7ErrorCategory.Protocol,
            S7ErrorCode.InvalidS7WStringData,
            S7OperationStage.Read,
            $"S7WString 字符数据非法：请求容量 {capacity} 码元，声明长度 {declaredLength} 码元，"
            + $"失败码元索引 {index}，{reason}。");
    }

    private static void EnsureLength(ReadOnlySpan<byte> data, int expected, S7DataType dataType)
    {
        if (data.Length < expected)
        {
            throw new S7ClientException(
                S7ErrorCategory.Protocol,
                S7ErrorCode.ResponseTooShort,
                S7OperationStage.Read,
                $"数据类型 {dataType} 解码失败：期望至少 {expected} 字节，实际 {data.Length} 字节。");
        }
    }

    private static void EnsureAscii(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > 0x7F)
            {
                throw new S7ClientException(
                    S7ErrorCategory.Protocol,
                    S7ErrorCode.NonAsciiStringData,
                    S7OperationStage.Read,
                    $"读回字符串在第 {i} 个字节包含非 ASCII 数据 0x{data[i]:X2}，拒绝返回部分值。");
            }
        }
    }

    private static S7ClientException InvalidHeader(string message)
    {
        return new S7ClientException(
            S7ErrorCategory.Protocol,
            S7ErrorCode.InvalidS7StringHeader,
            S7OperationStage.Read,
            message);
    }
}