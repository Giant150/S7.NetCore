using S7.NetCore;

namespace S7.NetCore.Tests.Values;

public sealed class S7ValueTest
{
    [Fact]
    public void Factories_ExposeStoredValueAndDataType()
    {
        S7Value boolean = S7Value.FromBool(true);
        Assert.True(boolean.AsBoolean());
        Assert.Equal(S7DataType.Bool, boolean.DataType);

        S7Value byteValue = S7Value.FromByte(200);
        Assert.Equal((byte)200, byteValue.AsByte());
        Assert.Equal(S7DataType.Byte, byteValue.DataType);

        S7Value int16 = S7Value.FromInt16(-32768);
        Assert.Equal((short)-32768, int16.AsInt16());
        Assert.Equal(S7DataType.Int, int16.DataType);

        S7Value uint16 = S7Value.FromUInt16(65535);
        Assert.Equal((ushort)65535, uint16.AsUInt16());
        Assert.Equal(S7DataType.Word, uint16.DataType);

        S7Value int32 = S7Value.FromInt32(-2147483648);
        Assert.Equal(int.MinValue, int32.AsInt32());
        Assert.Equal(S7DataType.DInt, int32.DataType);

        S7Value uint32 = S7Value.FromUInt32(4294967295);
        Assert.Equal(uint.MaxValue, uint32.AsUInt32());
        Assert.Equal(S7DataType.DWord, uint32.DataType);

        S7Value int64 = S7Value.FromInt64(long.MinValue);
        Assert.Equal(S7DataType.DInt, int64.DataType);
        Assert.Equal(long.MinValue, int64.GetSignedInteger());

        S7Value uint64 = S7Value.FromUInt64(ulong.MaxValue);
        Assert.Equal(S7DataType.DWord, uint64.DataType);
        Assert.Equal(ulong.MaxValue, uint64.GetUnsignedInteger());

        S7Value single = S7Value.FromSingle(1.5f);
        Assert.Equal(1.5f, single.AsSingle());
        Assert.Equal(S7DataType.Real, single.DataType);

        S7Value doubleValue = S7Value.FromDouble(2.5);
        Assert.Equal(2.5, doubleValue.AsDouble());
        Assert.Equal(S7DataType.LReal, doubleValue.DataType);

        S7Value text = S7Value.FromString("S7-PLC");
        Assert.Equal("S7-PLC", text.AsString());
        Assert.Equal(S7DataType.String, text.DataType);

        S7Value emptyText = S7Value.FromString(string.Empty);
        Assert.Equal(string.Empty, emptyText.AsString());
    }

    [Fact]
    public void FromString_Null_Throws()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => S7Value.FromString(null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ToValue_ReturnsStoredValueForEverySupportedType()
    {
        Assert.True(S7Value.FromBool(true).ToValue<bool>());
        Assert.Equal((byte)200, S7Value.FromByte(200).ToValue<byte>());
        Assert.Equal((short)-20000, S7Value.FromInt16(-20000).ToValue<short>());
        Assert.Equal((ushort)54321, S7Value.FromUInt16(54321).ToValue<ushort>());
        Assert.Equal(-123456789, S7Value.FromInt32(-123456789).ToValue<int>());
        Assert.Equal(1234567890u, S7Value.FromUInt32(1234567890).ToValue<uint>());
        Assert.Equal(long.MinValue, S7Value.FromInt64(long.MinValue).ToValue<long>());
        Assert.Equal(ulong.MaxValue, S7Value.FromUInt64(ulong.MaxValue).ToValue<ulong>());
        Assert.Equal(3.25f, S7Value.FromSingle(3.25f).ToValue<float>());
        Assert.Equal(4.5, S7Value.FromDouble(4.5).ToValue<double>());
        Assert.Equal("Pump-01", S7Value.FromString("Pump-01").ToValue<string>());
        Assert.Equal(string.Empty, S7Value.FromString(string.Empty).ToValue<string>());
    }

    [Fact]
    public void ToValue_KindMismatch_ThrowsDescribingExpectedAndActualKinds()
    {
        AssertToValueMismatch("System.Int32", "System.Boolean", () => { _ = S7Value.FromBool(true).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.Byte", () => { _ = S7Value.FromByte(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.Int16", () => { _ = S7Value.FromInt16(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.UInt16", () => { _ = S7Value.FromUInt16(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int64", "System.Int32", () => { _ = S7Value.FromInt32(1).ToValue<long>(); });
        AssertToValueMismatch("System.Int32", "System.UInt32", () => { _ = S7Value.FromUInt32(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.Int64", () => { _ = S7Value.FromInt64(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.UInt64", () => { _ = S7Value.FromUInt64(1).ToValue<int>(); });
        AssertToValueMismatch("System.Double", "System.Single", () => { _ = S7Value.FromSingle(1).ToValue<double>(); });
        AssertToValueMismatch("System.Int32", "System.Double", () => { _ = S7Value.FromDouble(1).ToValue<int>(); });
        AssertToValueMismatch("System.Int32", "System.String", () => { _ = S7Value.FromString("x").ToValue<int>(); });
    }

    [Fact]
    public void ToValue_UnsupportedTargetType_Throws()
    {
        InvalidOperationException boxed = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromInt32(1).ToValue<object>(); });
        Assert.Contains("System.Object", boxed.Message);

        InvalidOperationException nullable = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromInt32(1).ToValue<int?>(); });
        Assert.Contains("System.Nullable", nullable.Message);

        InvalidOperationException decimalTarget = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromInt32(1).ToValue<decimal>(); });
        Assert.Contains("System.Decimal", decimalTarget.Message);
    }

    [Fact]
    public void ToValue_OnEmptyInstance_ThrowsNotCarriedError()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => { _ = default(S7Value).ToValue<int>(); });

        Assert.Contains("未承载值", exception.Message);
        Assert.Contains("System.Int32", exception.Message);
    }

    [Fact]
    public void TryToValue_UnsupportedType_ReturnsFalseWithoutThrowing()
    {
        S7Value value = S7Value.FromInt32(7);

        Assert.False(value.TryToValue(out object? boxed));
        Assert.Null(boxed);

        Assert.False(value.TryToValue(out decimal decimalValue));
        Assert.Equal(0m, decimalValue);

        Assert.False(value.TryToValue(out int? nullable));
        Assert.Null(nullable);
    }

    [Fact]
    public void TryToValue_OnEmptyInstance_ReturnsFalseAndDefaultPerType()
    {
        S7Value empty = default;

        Assert.False(empty.TryToValue(out bool boolean));
        Assert.False(boolean);

        Assert.False(empty.TryToValue(out byte byteValue));
        Assert.Equal((byte)0, byteValue);

        Assert.False(empty.TryToValue(out short int16));
        Assert.Equal((short)0, int16);

        Assert.False(empty.TryToValue(out ushort uint16));
        Assert.Equal((ushort)0, uint16);

        Assert.False(empty.TryToValue(out int int32));
        Assert.Equal(0, int32);

        Assert.False(empty.TryToValue(out uint uint32));
        Assert.Equal(0u, uint32);

        Assert.False(empty.TryToValue(out long int64));
        Assert.Equal(0L, int64);

        Assert.False(empty.TryToValue(out ulong uint64));
        Assert.Equal(0UL, uint64);

        Assert.False(empty.TryToValue(out float single));
        Assert.Equal(0f, single);

        Assert.False(empty.TryToValue(out double doubleValue));
        Assert.Equal(0d, doubleValue);

        Assert.False(empty.TryToValue(out string text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void TryToValue_Success_ReturnsTrueAndStoredValue()
    {
        Assert.True(S7Value.FromBool(false).TryToValue(out bool boolean));
        Assert.False(boolean);

        Assert.True(S7Value.FromByte(255).TryToValue(out byte byteValue));
        Assert.Equal((byte)255, byteValue);

        Assert.True(S7Value.FromInt16(-123).TryToValue(out short int16));
        Assert.Equal((short)-123, int16);

        Assert.True(S7Value.FromUInt16(123).TryToValue(out ushort uint16));
        Assert.Equal((ushort)123, uint16);

        Assert.True(S7Value.FromInt32(-123456).TryToValue(out int int32));
        Assert.Equal(-123456, int32);

        Assert.True(S7Value.FromUInt32(123456).TryToValue(out uint uint32));
        Assert.Equal(123456u, uint32);

        Assert.True(S7Value.FromInt64(-1234567890123).TryToValue(out long int64));
        Assert.Equal(-1234567890123L, int64);

        Assert.True(S7Value.FromUInt64(1234567890123).TryToValue(out ulong uint64));
        Assert.Equal(1234567890123UL, uint64);

        Assert.True(S7Value.FromSingle(0.5f).TryToValue(out float single));
        Assert.Equal(0.5f, single);

        Assert.True(S7Value.FromDouble(0.25).TryToValue(out double doubleValue));
        Assert.Equal(0.25, doubleValue);

        Assert.True(S7Value.FromString("Tank").TryToValue(out string text));
        Assert.Equal("Tank", text);

        Assert.True(S7Value.FromString(string.Empty).TryToValue(out string emptyText));
        Assert.Equal(string.Empty, emptyText);
    }

    [Fact]
    public void FromValue_TypedSource_MatchesDedicatedFactory()
    {
        Assert.Equal(S7DataType.Bool, S7Value.FromValue(true).DataType);
        Assert.True(S7Value.FromValue(true).AsBoolean());

        Assert.Equal(S7DataType.Byte, S7Value.FromValue((byte)9).DataType);
        Assert.Equal((byte)9, S7Value.FromValue((byte)9).AsByte());

        Assert.Equal(S7DataType.Int, S7Value.FromValue((short)-9).DataType);
        Assert.Equal((short)-9, S7Value.FromValue((short)-9).AsInt16());

        Assert.Equal(S7DataType.Word, S7Value.FromValue((ushort)9).DataType);
        Assert.Equal((ushort)9, S7Value.FromValue((ushort)9).AsUInt16());

        Assert.Equal(S7DataType.DInt, S7Value.FromValue(-9).DataType);
        Assert.Equal(-9, S7Value.FromValue(-9).AsInt32());

        Assert.Equal(S7DataType.DWord, S7Value.FromValue(9u).DataType);
        Assert.Equal(9u, S7Value.FromValue(9u).AsUInt32());

        Assert.Equal(S7DataType.DInt, S7Value.FromValue(-9L).DataType);
        Assert.Equal(-9L, S7Value.FromValue(-9L).ToValue<long>());

        Assert.Equal(S7DataType.DWord, S7Value.FromValue(9UL).DataType);
        Assert.Equal(9UL, S7Value.FromValue(9UL).ToValue<ulong>());

        Assert.Equal(S7DataType.Real, S7Value.FromValue(1.5f).DataType);
        Assert.Equal(1.5f, S7Value.FromValue(1.5f).AsSingle());

        Assert.Equal(S7DataType.LReal, S7Value.FromValue(2.5).DataType);
        Assert.Equal(2.5, S7Value.FromValue(2.5).AsDouble());

        Assert.Equal(S7DataType.String, S7Value.FromValue("V1").DataType);
        Assert.Equal("V1", S7Value.FromValue("V1").AsString());
    }

    [Fact]
    public void FromValue_ObjectSource_UsesRuntimeType()
    {
        Assert.True(S7Value.FromValue<object>(true).AsBoolean());
        Assert.Equal((byte)1, S7Value.FromValue<object>((byte)1).AsByte());
        Assert.Equal((short)2, S7Value.FromValue<object>((short)2).AsInt16());
        Assert.Equal((ushort)3, S7Value.FromValue<object>((ushort)3).AsUInt16());
        Assert.Equal(4, S7Value.FromValue<object>(4).AsInt32());
        Assert.Equal(5u, S7Value.FromValue<object>(5u).AsUInt32());
        Assert.Equal(6L, S7Value.FromValue<object>(6L).ToValue<long>());
        Assert.Equal(7UL, S7Value.FromValue<object>(7UL).ToValue<ulong>());
        Assert.Equal(1.25f, S7Value.FromValue<object>(1.25f).AsSingle());
        Assert.Equal(2.75, S7Value.FromValue<object>(2.75).AsDouble());
        Assert.Equal("Valve", S7Value.FromValue<object>("Valve").AsString());
    }

    [Fact]
    public void FromValue_NullableSource_BuildsFromUnderlyingType()
    {
        S7Value fromNullable = S7Value.FromValue<int?>(5);
        Assert.Equal(S7DataType.DInt, fromNullable.DataType);
        Assert.Equal(5, fromNullable.ToValue<int>());

        Assert.True(S7Value.FromValue<bool?>(true).AsBoolean());
        Assert.Equal((byte)8, S7Value.FromValue<byte?>(8).AsByte());
        Assert.Equal((short)-8, S7Value.FromValue<short?>(-8).AsInt16());
        Assert.Equal((ushort)8, S7Value.FromValue<ushort?>(8).AsUInt16());
        Assert.Equal(8u, S7Value.FromValue<uint?>(8).AsUInt32());
        Assert.Equal(8L, S7Value.FromValue<long?>(8L).ToValue<long>());
        Assert.Equal(8UL, S7Value.FromValue<ulong?>(8UL).ToValue<ulong>());
        Assert.Equal(0.5f, S7Value.FromValue<float?>(0.5f).AsSingle());
        Assert.Equal(0.25, S7Value.FromValue<double?>(0.25).AsDouble());
    }

    [Fact]
    public void FromValue_NullOrUnsupportedInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = S7Value.FromValue<string?>(null); });
        Assert.Throws<ArgumentNullException>(() => { _ = S7Value.FromValue<object?>(null); });
        Assert.Throws<ArgumentNullException>(() => { _ = S7Value.FromValue<int?>(null); });
        Assert.Throws<ArgumentNullException>(() => { _ = S7Value.FromValue<bool?>(null); });

        InvalidOperationException decimalSource = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromValue(1.5m); });
        Assert.Contains("System.Decimal", decimalSource.Message);

        InvalidOperationException dateTimeSource = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromValue(DateTime.UnixEpoch); });
        Assert.Contains("System.DateTime", dateTimeSource.Message);

        InvalidOperationException runtimeType = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromValue<object>(new Uri("https://example.org")); });
        Assert.Contains("System.Uri", runtimeType.Message);

        InvalidOperationException unsupportedReference = Assert.Throws<InvalidOperationException>(
            () => { _ = S7Value.FromValue(new Uri("https://example.org")); });
        Assert.Contains("System.Uri", unsupportedReference.Message);
    }

    [Fact]
    public void AsMethods_Mismatch_ThrowAndDescribeTypeAnnotation()
    {
        S7Value text = S7Value.FromString("x");

        Assert.Contains("S7Value 不是 Boolean 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsBoolean(); }).Message);
        Assert.Contains("S7Value 不是 Byte 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsByte(); }).Message);
        Assert.Contains("S7Value 不是 Int16 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsInt16(); }).Message);
        Assert.Contains("S7Value 不是 UInt16 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsUInt16(); }).Message);
        Assert.Contains("S7Value 不是 Int32 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsInt32(); }).Message);
        Assert.Contains("S7Value 不是 UInt32 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsUInt32(); }).Message);
        Assert.Contains("S7Value 不是 Single 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsSingle(); }).Message);
        Assert.Contains("S7Value 不是 Double 值", Assert.Throws<InvalidOperationException>(() => { _ = text.AsDouble(); }).Message);
        Assert.Contains("S7Value 不是 String 值", Assert.Throws<InvalidOperationException>(() => { _ = S7Value.FromInt32(1).AsString(); }).Message);
    }

    [Fact]
    public void InternalKindPredicates_ClassifyStoredValue()
    {
        S7Value boolean = S7Value.FromBool(true);
        Assert.True(boolean.IsBoolean);
        Assert.False(boolean.IsInteger);
        Assert.False(boolean.IsSingle);
        Assert.False(boolean.IsDouble);
        Assert.False(boolean.IsString);

        AssertInteger(S7Value.FromByte(1), signed: false);
        AssertInteger(S7Value.FromUInt16(1), signed: false);
        AssertInteger(S7Value.FromUInt32(1), signed: false);
        AssertInteger(S7Value.FromUInt64(1), signed: false);
        AssertInteger(S7Value.FromInt16(1), signed: true);
        AssertInteger(S7Value.FromInt32(1), signed: true);
        AssertInteger(S7Value.FromInt64(1), signed: true);

        S7Value single = S7Value.FromSingle(1);
        Assert.True(single.IsSingle);
        Assert.False(single.IsDouble);
        Assert.False(single.IsInteger);

        S7Value doubleValue = S7Value.FromDouble(1);
        Assert.True(doubleValue.IsDouble);
        Assert.False(doubleValue.IsSingle);

        S7Value text = S7Value.FromString("1");
        Assert.True(text.IsString);
        Assert.False(text.IsInteger);
    }

    [Fact]
    public void InternalValueAccessors_WidenIntegersAndExposeFloats()
    {
        Assert.Equal(-5L, S7Value.FromInt16(-5).GetSignedInteger());
        Assert.Equal(-6L, S7Value.FromInt32(-6).GetSignedInteger());
        Assert.Equal(-7L, S7Value.FromInt64(-7).GetSignedInteger());
        Assert.Equal(8L, S7Value.FromByte(8).GetSignedInteger());
        Assert.Equal(9L, S7Value.FromUInt16(9).GetSignedInteger());
        Assert.Equal(10L, S7Value.FromUInt32(10).GetSignedInteger());
        Assert.Equal(11L, S7Value.FromUInt64(11).GetSignedInteger());

        Assert.Equal(12UL, S7Value.FromByte(12).GetUnsignedInteger());
        Assert.Equal(13UL, S7Value.FromUInt16(13).GetUnsignedInteger());
        Assert.Equal(14UL, S7Value.FromUInt32(14).GetUnsignedInteger());
        Assert.Equal(15UL, S7Value.FromUInt64(15).GetUnsignedInteger());
        Assert.Equal(16UL, S7Value.FromInt16(16).GetUnsignedInteger());
        Assert.Equal(17UL, S7Value.FromInt32(17).GetUnsignedInteger());
        Assert.Equal(18UL, S7Value.FromInt64(18).GetUnsignedInteger());

        Assert.Equal(1.5f, S7Value.FromSingle(1.5f).GetSingleValue());
        Assert.Equal(2.5, S7Value.FromDouble(2.5).GetDoubleValue());
        Assert.Equal("Level", S7Value.FromString("Level").GetStringValue());

        Assert.Throws<OverflowException>(() => S7Value.FromUInt64(ulong.MaxValue).GetSignedInteger());
    }

    [Fact]
    public void InternalValueAccessors_RejectWrongKindWithTypeAnnotationMessage()
    {
        InvalidOperationException notInteger = Assert.Throws<InvalidOperationException>(
            () => S7Value.FromSingle(1).GetSignedInteger());
        Assert.Contains("S7Value 不是 Integer 值", notInteger.Message);
        Assert.Contains("Real", notInteger.Message);

        InvalidOperationException booleanAsUnsigned = Assert.Throws<InvalidOperationException>(
            () => S7Value.FromBool(true).GetUnsignedInteger());
        Assert.Contains("S7Value 不是 Integer 值", booleanAsUnsigned.Message);
        Assert.Contains("Bool", booleanAsUnsigned.Message);

        InvalidOperationException negativeAsUnsigned = Assert.Throws<InvalidOperationException>(
            () => S7Value.FromInt16(-1).GetUnsignedInteger());
        Assert.Contains("S7Value 不是 Unsigned integer 值", negativeAsUnsigned.Message);
        Assert.Contains("Int", negativeAsUnsigned.Message);

        Assert.Throws<InvalidOperationException>(() => S7Value.FromDouble(1).GetSingleValue());
        Assert.Throws<InvalidOperationException>(() => S7Value.FromSingle(1).GetDoubleValue());
        Assert.Throws<InvalidOperationException>(() => S7Value.FromInt32(1).GetStringValue());
    }

    private static void AssertToValueMismatch(string expectedTypeName, string actualTypeName, Action action)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(action);

        Assert.Contains(expectedTypeName, exception.Message);
        Assert.Contains(actualTypeName, exception.Message);
        Assert.Contains("实际构造来源种类", exception.Message);
    }

    private static void AssertInteger(S7Value value, bool signed)
    {
        Assert.True(value.IsInteger);
        Assert.Equal(signed, value.IsSignedInteger);
        Assert.Equal(!signed, value.IsUnsignedInteger);
        Assert.False(value.IsBoolean);
        Assert.False(value.IsSingle);
        Assert.False(value.IsDouble);
        Assert.False(value.IsString);
    }
}