namespace S7.NetCore;

/// <summary>
/// 类型化值对象。只承载十一种 S7 数据类型，不提供 object/dynamic 入口。
/// <para>
/// <see cref="DataType"/> 是值来源的默认类型标注，最终写入类型由 <see cref="S7TypedWriteRequest.DataType"/> 决定。
/// 整数工厂允许宽化使用（例如用 <see cref="FromInt64"/> 构造后写入 Int<span/>），范围由请求校验负责。
/// </para>
/// </summary>
public readonly struct S7Value
{
    private enum ValueKind : byte
    {
        None = 0,
        Boolean = 1,
        Byte = 2,
        Int16 = 3,
        UInt16 = 4,
        Int32 = 5,
        UInt32 = 6,
        Int64 = 7,
        UInt64 = 8,
        Single = 9,
        Double = 10,
        String = 11
    }

    private readonly ValueKind _kind;
    private readonly S7DataType _dataType;
    private readonly bool _booleanValue;
    private readonly long _signedValue;
    private readonly ulong _unsignedValue;
    private readonly float _singleValue;
    private readonly double _doubleValue;
    private readonly string? _stringValue;

    private S7Value(
        ValueKind kind,
        S7DataType dataType,
        bool booleanValue = false,
        long signedValue = 0,
        ulong unsignedValue = 0,
        float singleValue = 0,
        double doubleValue = 0,
        string? stringValue = null)
    {
        _kind = kind;
        _dataType = dataType;
        _booleanValue = booleanValue;
        _signedValue = signedValue;
        _unsignedValue = unsignedValue;
        _singleValue = singleValue;
        _doubleValue = doubleValue;
        _stringValue = stringValue;
    }

    /// <summary>值来源的默认类型标注。</summary>
    public S7DataType DataType => _dataType;

    /// <summary>构造布尔值。</summary>
    /// <param name="value">布尔值。</param>
    /// <returns>类型标注为 <see cref="S7DataType.Bool"/> 的值。</returns>
    public static S7Value FromBool(bool value)
    {
        return new S7Value(ValueKind.Boolean, S7DataType.Bool, booleanValue: value);
    }

    /// <summary>构造无符号 8 位整数。</summary>
    /// <param name="value">取值范围 0～255。</param>
    /// <returns>类型标注为 <see cref="S7DataType.Byte"/> 的值。</returns>
    public static S7Value FromByte(byte value)
    {
        return new S7Value(ValueKind.Byte, S7DataType.Byte, unsignedValue: value);
    }

    /// <summary>构造有符号 16 位整数。</summary>
    /// <param name="value">取值范围 -32768～32767。</param>
    /// <returns>类型标注为 <see cref="S7DataType.Int"/> 的值。</returns>
    public static S7Value FromInt16(short value)
    {
        return new S7Value(ValueKind.Int16, S7DataType.Int, signedValue: value);
    }

    /// <summary>构造无符号 16 位整数。</summary>
    /// <param name="value">取值范围 0～65535。</param>
    /// <returns>类型标注为 <see cref="S7DataType.Word"/> 的值。</returns>
    public static S7Value FromUInt16(ushort value)
    {
        return new S7Value(ValueKind.UInt16, S7DataType.Word, unsignedValue: value);
    }

    /// <summary>构造有符号 32 位整数。</summary>
    /// <param name="value">取值范围 -2147483648～2147483647。</param>
    /// <returns>类型标注为 <see cref="S7DataType.DInt"/> 的值。</returns>
    public static S7Value FromInt32(int value)
    {
        return new S7Value(ValueKind.Int32, S7DataType.DInt, signedValue: value);
    }

    /// <summary>构造无符号 32 位整数。</summary>
    /// <param name="value">取值范围 0～4294967295。</param>
    /// <returns>类型标注为 <see cref="S7DataType.DWord"/> 的值。</returns>
    public static S7Value FromUInt32(uint value)
    {
        return new S7Value(ValueKind.UInt32, S7DataType.DWord, unsignedValue: value);
    }

    /// <summary>构造有符号 64 位整数，用于整数宽化输入（例如写入 Int/Word/DInt 时由目标类型校验范围）。</summary>
    /// <param name="value">有符号 64 位整数。</param>
    /// <returns>类型标注为 <see cref="S7DataType.DInt"/> 的值。</returns>
    public static S7Value FromInt64(long value)
    {
        return new S7Value(ValueKind.Int64, S7DataType.DInt, signedValue: value);
    }

    /// <summary>构造无符号 64 位整数，用于整数宽化输入。</summary>
    /// <param name="value">无符号 64 位整数。</param>
    /// <returns>类型标注为 <see cref="S7DataType.DWord"/> 的值。</returns>
    public static S7Value FromUInt64(ulong value)
    {
        return new S7Value(ValueKind.UInt64, S7DataType.DWord, unsignedValue: value);
    }

    /// <summary>构造单精度浮点数。</summary>
    /// <param name="value">单精度值。</param>
    /// <returns>类型标注为 <see cref="S7DataType.Real"/> 的值。</returns>
    public static S7Value FromSingle(float value)
    {
        return new S7Value(ValueKind.Single, S7DataType.Real, singleValue: value);
    }

    /// <summary>构造双精度浮点数。</summary>
    /// <param name="value">双精度值。</param>
    /// <returns>类型标注为 <see cref="S7DataType.LReal"/> 的值。</returns>
    public static S7Value FromDouble(double value)
    {
        return new S7Value(ValueKind.Double, S7DataType.LReal, doubleValue: value);
    }

    /// <summary>构造 ASCII 字符串。</summary>
    /// <param name="value">字符串值，不得为 <see langword="null"/>。</param>
    /// <returns>类型标注为 <see cref="S7DataType.String"/> 的值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
    public static S7Value FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new S7Value(ValueKind.String, S7DataType.String, stringValue: value);
    }

    /// <summary>获取布尔值。</summary>
    /// <returns>布尔值。</returns>
    /// <exception cref="InvalidOperationException">值的种类不是布尔。</exception>
    public bool AsBoolean()
    {
        return _kind == ValueKind.Boolean ? _booleanValue : throw Mismatch("Boolean");
    }

    /// <summary>获取无符号 8 位整数。</summary>
    /// <returns>字节值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromByte"/> 构造。</exception>
    public byte AsByte()
    {
        return _kind == ValueKind.Byte ? (byte)_unsignedValue : throw Mismatch("Byte");
    }

    /// <summary>获取有符号 16 位整数。</summary>
    /// <returns>16 位有符号值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromInt16"/> 构造。</exception>
    public short AsInt16()
    {
        return _kind == ValueKind.Int16 ? (short)_signedValue : throw Mismatch("Int16");
    }

    /// <summary>获取无符号 16 位整数。</summary>
    /// <returns>16 位无符号值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromUInt16"/> 构造。</exception>
    public ushort AsUInt16()
    {
        return _kind == ValueKind.UInt16 ? (ushort)_unsignedValue : throw Mismatch("UInt16");
    }

    /// <summary>获取有符号 32 位整数。</summary>
    /// <returns>32 位有符号值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromInt32"/> 构造。</exception>
    public int AsInt32()
    {
        return _kind == ValueKind.Int32 ? (int)_signedValue : throw Mismatch("Int32");
    }

    /// <summary>获取无符号 32 位整数。</summary>
    /// <returns>32 位无符号值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromUInt32"/> 构造。</exception>
    public uint AsUInt32()
    {
        return _kind == ValueKind.UInt32 ? (uint)_unsignedValue : throw Mismatch("UInt32");
    }

    /// <summary>获取单精度浮点数。</summary>
    /// <returns>单精度值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromSingle"/> 构造。</exception>
    public float AsSingle()
    {
        return _kind == ValueKind.Single ? _singleValue : throw Mismatch("Single");
    }

    /// <summary>获取双精度浮点数。</summary>
    /// <returns>双精度值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromDouble"/> 构造。</exception>
    public double AsDouble()
    {
        return _kind == ValueKind.Double ? _doubleValue : throw Mismatch("Double");
    }

    /// <summary>获取字符串值。</summary>
    /// <returns>字符串值。</returns>
    /// <exception cref="InvalidOperationException">值不是由 <see cref="FromString"/> 构造。</exception>
    public string AsString()
    {
        return _kind == ValueKind.String ? _stringValue! : throw Mismatch("String");
    }

    internal bool IsBoolean => _kind == ValueKind.Boolean;

    internal bool IsInteger => _kind is ValueKind.Byte
        or ValueKind.Int16
        or ValueKind.UInt16
        or ValueKind.Int32
        or ValueKind.UInt32
        or ValueKind.Int64
        or ValueKind.UInt64;

    internal bool IsSignedInteger => _kind is ValueKind.Int16 or ValueKind.Int32 or ValueKind.Int64;

    internal bool IsUnsignedInteger => _kind is ValueKind.Byte or ValueKind.UInt16 or ValueKind.UInt32 or ValueKind.UInt64;

    internal bool IsSingle => _kind == ValueKind.Single;

    internal bool IsDouble => _kind == ValueKind.Double;

    internal bool IsString => _kind == ValueKind.String;

    internal long GetSignedInteger()
    {
        return _kind switch
        {
            ValueKind.Int16 or ValueKind.Int32 or ValueKind.Int64 => _signedValue,
            ValueKind.Byte or ValueKind.UInt16 or ValueKind.UInt32 or ValueKind.UInt64 => checked((long)_unsignedValue),
            _ => throw Mismatch("Integer")
        };
    }

    internal ulong GetUnsignedInteger()
    {
        return _kind switch
        {
            ValueKind.Byte or ValueKind.UInt16 or ValueKind.UInt32 or ValueKind.UInt64 => _unsignedValue,
            ValueKind.Int16 or ValueKind.Int32 or ValueKind.Int64 => _signedValue >= 0
                ? (ulong)_signedValue
                : throw Mismatch("Unsigned integer"),
            _ => throw Mismatch("Integer")
        };
    }

    internal float GetSingleValue()
    {
        return _kind == ValueKind.Single ? _singleValue : throw Mismatch("Single");
    }

    internal double GetDoubleValue()
    {
        return _kind == ValueKind.Double ? _doubleValue : throw Mismatch("Double");
    }

    internal string GetStringValue()
    {
        return _kind == ValueKind.String ? _stringValue! : throw Mismatch("String");
    }

    private InvalidOperationException Mismatch(string expected)
    {
        return new InvalidOperationException($"S7Value 不是 {expected} 值，实际类型标注为 {_dataType}。");
    }
}