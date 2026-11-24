using System.Runtime.CompilerServices;

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

    /// <summary>构造带 S7String 编码标注的字符串值。</summary>
    /// <param name="value">字符串值，不得为 <see langword="null"/>。</param>
    /// <returns>承载种类为字符串、标注为 <see cref="S7DataType.S7String"/> 的值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
    public static S7Value FromS7String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new S7Value(ValueKind.String, S7DataType.S7String, stringValue: value);
    }

    /// <summary>构造带 S7WString 编码标注的字符串值。</summary>
    /// <param name="value">字符串值，不得为 <see langword="null"/>。</param>
    /// <returns>承载种类为字符串、标注为 <see cref="S7DataType.S7WString"/> 的值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
    public static S7Value FromS7WString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new S7Value(ValueKind.String, S7DataType.S7WString, stringValue: value);
    }

    /// <summary>值是否以字符串种类承载（String/S7String/S7WString 均为真）。</summary>
    public bool IsStringValue => _kind == ValueKind.String;

    /// <summary>
    /// 字符串编码标注；非字符串为 <see langword="null"/>。
    /// 返回 <see cref="S7DataType.String"/>、<see cref="S7DataType.S7String"/> 或 <see cref="S7DataType.S7WString"/>。
    /// </summary>
    public S7DataType? StringEncoding => _kind == ValueKind.String ? _dataType : null;

    /// <summary>
    /// 按类型参数构造值对象，支持十一种承载类型、<see cref="object"/> 运行时类型识别与非空可空值类型。
    /// </summary>
    /// <typeparam name="T">
    /// 承载类型（<see cref="bool"/>、<see cref="byte"/>、<see cref="short"/>、<see cref="ushort"/>、
    /// <see cref="int"/>、<see cref="uint"/>、<see cref="long"/>、<see cref="ulong"/>、<see cref="float"/>、
    /// <see cref="double"/>、<see cref="string"/>）、<see cref="object"/> 或非空可空值类型。
    /// <see cref="Nullable{T}"/> 仅允许在构造侧以非空值出现，不作为取值目标。
    /// </typeparam>
    /// <param name="value">待构造的值，不得为 <see langword="null"/>。</param>
    /// <returns>类型标注与对应强类型工厂一致的值对象。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> 或运行时值不是支持的承载类型。</exception>
    public static S7Value FromValue<T>(T value)
    {
        if (typeof(T) == typeof(bool)) return FromBool(Unsafe.As<T, bool>(ref value));
        if (typeof(T) == typeof(byte)) return FromByte(Unsafe.As<T, byte>(ref value));
        if (typeof(T) == typeof(short)) return FromInt16(Unsafe.As<T, short>(ref value));
        if (typeof(T) == typeof(ushort)) return FromUInt16(Unsafe.As<T, ushort>(ref value));
        if (typeof(T) == typeof(int)) return FromInt32(Unsafe.As<T, int>(ref value));
        if (typeof(T) == typeof(uint)) return FromUInt32(Unsafe.As<T, uint>(ref value));
        if (typeof(T) == typeof(long)) return FromInt64(Unsafe.As<T, long>(ref value));
        if (typeof(T) == typeof(ulong)) return FromUInt64(Unsafe.As<T, ulong>(ref value));
        if (typeof(T) == typeof(float)) return FromSingle(Unsafe.As<T, float>(ref value));
        if (typeof(T) == typeof(double)) return FromDouble(Unsafe.As<T, double>(ref value));

        if (typeof(T) == typeof(string))
        {
            string? text = Unsafe.As<T, string>(ref value);
            if (text is null) throw new ArgumentNullException(nameof(value));
            return FromString(text);
        }

        if (typeof(T) == typeof(object))
        {
            object? boxed = Unsafe.As<T, object>(ref value);
            if (boxed is null) throw new ArgumentNullException(nameof(value));
            return FromObject(boxed);
        }

        if (Nullable.GetUnderlyingType(typeof(T)) is not null)
        {
            object? boxed = value;
            if (boxed is null) throw new ArgumentNullException(nameof(value));
            return FromObject(boxed);
        }

        throw new InvalidOperationException($"不支持的类型 {typeof(T)}。");
    }

    private static S7Value FromObject(object value)
    {
        return value switch
        {
            bool boolValue => FromBool(boolValue),
            byte byteValue => FromByte(byteValue),
            short shortValue => FromInt16(shortValue),
            ushort ushortValue => FromUInt16(ushortValue),
            int intValue => FromInt32(intValue),
            uint uintValue => FromUInt32(uintValue),
            long longValue => FromInt64(longValue),
            ulong ulongValue => FromUInt64(ulongValue),
            float floatValue => FromSingle(floatValue),
            double doubleValue => FromDouble(doubleValue),
            string stringValue => FromString(stringValue),
            _ => throw new InvalidOperationException($"不支持的类型 {value.GetType()}。")
        };
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

    /// <summary>
    /// 按构造来源种类取回与类型参数一致的 CLR 值（对应 <see cref="AsBoolean"/> 等强类型取值方法的泛型入口）。
    /// </summary>
    /// <typeparam name="T">
    /// 十一种承载类型之一：<see cref="bool"/>、<see cref="byte"/>、<see cref="short"/>、<see cref="ushort"/>、
    /// <see cref="int"/>、<see cref="uint"/>、<see cref="long"/>、<see cref="ulong"/>、<see cref="float"/>、
    /// <see cref="double"/>、<see cref="string"/>。
    /// <see cref="Nullable{T}"/> 与 <see cref="object"/> 不受支持。
    /// </typeparam>
    /// <returns>与 <typeparamref name="T"/> 一致的值；字符串返回非空引用，空串为 <see cref="string.Empty"/>。</returns>
    /// <exception cref="InvalidOperationException">
    /// 目标类型不受支持、实例未承载值，或构造来源种类与 <typeparamref name="T"/> 严格不匹配。
    /// </exception>
    public T ToValue<T>()
    {
        if (typeof(T) == typeof(bool)) return ToBoolean<T>();
        if (typeof(T) == typeof(byte)) return ToByte<T>();
        if (typeof(T) == typeof(short)) return ToInt16<T>();
        if (typeof(T) == typeof(ushort)) return ToUInt16<T>();
        if (typeof(T) == typeof(int)) return ToInt32<T>();
        if (typeof(T) == typeof(uint)) return ToUInt32<T>();
        if (typeof(T) == typeof(long)) return ToInt64<T>();
        if (typeof(T) == typeof(ulong)) return ToUInt64<T>();
        if (typeof(T) == typeof(float)) return ToSingle<T>();
        if (typeof(T) == typeof(double)) return ToDouble<T>();
        if (typeof(T) == typeof(string)) return ToStringValue<T>();
        throw new InvalidOperationException($"不支持的类型 {typeof(T)}。");
    }

    /// <summary>
    /// 探测式泛型取值。种类不匹配、实例未承载值或目标类型不受支持时返回 <see langword="false"/>，不抛出异常。
    /// </summary>
    /// <typeparam name="T">目标类型，取值口径与 <see cref="ToValue{T}"/> 一致。</typeparam>
    /// <param name="value">
    /// 成功时为取值结果；失败时为 <typeparamref name="T"/> 的默认值。
    /// 字符串失败时输出 <see cref="string.Empty"/>；不受支持的类型（含 <see cref="Nullable{T}"/>）输出 <see langword="default"/>。
    /// </param>
    /// <returns>成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    public bool TryToValue<T>(out T value)
    {
        value = default!;
        if (typeof(T) == typeof(bool))
        {
            if (_kind != ValueKind.Boolean) return false;
            bool boolValue = _booleanValue;
            value = Unsafe.As<bool, T>(ref boolValue);
            return true;
        }

        if (typeof(T) == typeof(byte))
        {
            if (_kind != ValueKind.Byte) return false;
            byte byteValue = (byte)_unsignedValue;
            value = Unsafe.As<byte, T>(ref byteValue);
            return true;
        }

        if (typeof(T) == typeof(short))
        {
            if (_kind != ValueKind.Int16) return false;
            short shortValue = (short)_signedValue;
            value = Unsafe.As<short, T>(ref shortValue);
            return true;
        }

        if (typeof(T) == typeof(ushort))
        {
            if (_kind != ValueKind.UInt16) return false;
            ushort ushortValue = (ushort)_unsignedValue;
            value = Unsafe.As<ushort, T>(ref ushortValue);
            return true;
        }

        if (typeof(T) == typeof(int))
        {
            if (_kind != ValueKind.Int32) return false;
            int intValue = (int)_signedValue;
            value = Unsafe.As<int, T>(ref intValue);
            return true;
        }

        if (typeof(T) == typeof(uint))
        {
            if (_kind != ValueKind.UInt32) return false;
            uint uintValue = (uint)_unsignedValue;
            value = Unsafe.As<uint, T>(ref uintValue);
            return true;
        }

        if (typeof(T) == typeof(long))
        {
            if (_kind != ValueKind.Int64) return false;
            long longValue = _signedValue;
            value = Unsafe.As<long, T>(ref longValue);
            return true;
        }

        if (typeof(T) == typeof(ulong))
        {
            if (_kind != ValueKind.UInt64) return false;
            ulong ulongValue = _unsignedValue;
            value = Unsafe.As<ulong, T>(ref ulongValue);
            return true;
        }

        if (typeof(T) == typeof(float))
        {
            if (_kind != ValueKind.Single) return false;
            float floatValue = _singleValue;
            value = Unsafe.As<float, T>(ref floatValue);
            return true;
        }

        if (typeof(T) == typeof(double))
        {
            if (_kind != ValueKind.Double) return false;
            double doubleValue = _doubleValue;
            value = Unsafe.As<double, T>(ref doubleValue);
            return true;
        }

        if (typeof(T) == typeof(string))
        {
            if (_kind != ValueKind.String)
            {
                string empty = string.Empty;
                value = Unsafe.As<string, T>(ref empty);
                return false;
            }

            string text = _stringValue!;
            value = Unsafe.As<string, T>(ref text);
            return true;
        }

        return false;
    }

    private T ToBoolean<T>()
    {
        EnsureKind(ValueKind.Boolean);
        bool value = _booleanValue;
        return Unsafe.As<bool, T>(ref value);
    }

    private T ToByte<T>()
    {
        EnsureKind(ValueKind.Byte);
        byte value = (byte)_unsignedValue;
        return Unsafe.As<byte, T>(ref value);
    }

    private T ToInt16<T>()
    {
        EnsureKind(ValueKind.Int16);
        short value = (short)_signedValue;
        return Unsafe.As<short, T>(ref value);
    }

    private T ToUInt16<T>()
    {
        EnsureKind(ValueKind.UInt16);
        ushort value = (ushort)_unsignedValue;
        return Unsafe.As<ushort, T>(ref value);
    }

    private T ToInt32<T>()
    {
        EnsureKind(ValueKind.Int32);
        int value = (int)_signedValue;
        return Unsafe.As<int, T>(ref value);
    }

    private T ToUInt32<T>()
    {
        EnsureKind(ValueKind.UInt32);
        uint value = (uint)_unsignedValue;
        return Unsafe.As<uint, T>(ref value);
    }

    private T ToInt64<T>()
    {
        EnsureKind(ValueKind.Int64);
        long value = _signedValue;
        return Unsafe.As<long, T>(ref value);
    }

    private T ToUInt64<T>()
    {
        EnsureKind(ValueKind.UInt64);
        ulong value = _unsignedValue;
        return Unsafe.As<ulong, T>(ref value);
    }

    private T ToSingle<T>()
    {
        EnsureKind(ValueKind.Single);
        float value = _singleValue;
        return Unsafe.As<float, T>(ref value);
    }

    private T ToDouble<T>()
    {
        EnsureKind(ValueKind.Double);
        double value = _doubleValue;
        return Unsafe.As<double, T>(ref value);
    }

    private T ToStringValue<T>()
    {
        EnsureKind(ValueKind.String);
        string value = _stringValue!;
        return Unsafe.As<string, T>(ref value);
    }

    private void EnsureKind(ValueKind expected)
    {
        if (_kind == ValueKind.None)
        {
            throw new InvalidOperationException($"S7Value 未承载值，期望 {GetKindTypeName(expected)}。");
        }

        if (_kind != expected)
        {
            throw Mismatch(expected);
        }
    }

    private static string GetKindTypeName(ValueKind kind)
    {
        return kind switch
        {
            ValueKind.Boolean => "System.Boolean",
            ValueKind.Byte => "System.Byte",
            ValueKind.Int16 => "System.Int16",
            ValueKind.UInt16 => "System.UInt16",
            ValueKind.Int32 => "System.Int32",
            ValueKind.UInt32 => "System.UInt32",
            ValueKind.Int64 => "System.Int64",
            ValueKind.UInt64 => "System.UInt64",
            ValueKind.Single => "System.Single",
            ValueKind.Double => "System.Double",
            ValueKind.String => "System.String",
            _ => "未承载值"
        };
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

    private InvalidOperationException Mismatch(ValueKind expected)
    {
        return new InvalidOperationException(
            $"S7Value 不是 {GetKindTypeName(expected)} 值，实际构造来源种类为 {GetKindTypeName(_kind)}。");
    }

    private InvalidOperationException Mismatch(string expected)
    {
        return new InvalidOperationException($"S7Value 不是 {expected} 值，实际类型标注为 {_dataType}。");
    }
}