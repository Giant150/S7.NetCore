using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Typed;

public sealed class TypedValidationTests
{
    [Fact]
    public void UnsupportedDataType_IsRejected()
    {
        AssertInvalid(
            () => ValidateRead("DB1.DBW0", (S7DataType)99),
            S7ErrorCode.DataTypeNotSupported);
    }

    [Theory]
    [InlineData("DB1.DBB0", S7DataType.Word)]
    [InlineData("DB1.DBW0", S7DataType.Real)]
    [InlineData("DB1.DBD0", S7DataType.Word)]
    [InlineData("DB1.DBB0", S7DataType.Bool)]
    public void TypeWidthMismatch_IsRejected(string expression, S7DataType dataType)
    {
        AssertInvalid(
            () => ValidateRead(expression, dataType),
            S7ErrorCode.DataTypeAddressMismatch);
    }

    [Fact]
    public void IntegerRangeViolations_AreRejected()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(256)),
            S7ErrorCode.ValueOutOfRange);

        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(-1)),
            S7ErrorCode.ValueOutOfRange);

        AssertInvalid(
            () => ValidateWrite("DB1.DBW0", S7DataType.Word, S7Value.FromInt32(65536)),
            S7ErrorCode.ValueOutOfRange);

        AssertInvalid(
            () => ValidateWrite("DB1.DBW0", S7DataType.Int, S7Value.FromInt32(40000)),
            S7ErrorCode.ValueOutOfRange);

        AssertInvalid(
            () => ValidateWrite("DB1.DBD0", S7DataType.DWord, S7Value.FromInt64(-1)),
            S7ErrorCode.ValueOutOfRange);
    }

    [Fact]
    public void NonAsciiStringWrite_IsRejected()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.String, S7Value.FromString("PLČ"), capacity: 8),
            S7ErrorCode.StringEncodingInvalid);
    }

    [Fact]
    public void CapacityViolations_AreRejected()
    {
        AssertInvalid(
            () => ValidateRead("DB1.DBB0", S7DataType.String, capacity: 0),
            S7ErrorCode.StringCapacityInvalid);

        AssertInvalid(
            () => ValidateRead("DB1.DBB0", S7DataType.S7String, capacity: 255),
            S7ErrorCode.StringCapacityInvalid);

        AssertInvalid(
            () => ValidateRead("DB1.DBW0", S7DataType.Word, capacity: 4),
            S7ErrorCode.StringCapacityInvalid);

        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.String, S7Value.FromString("ABCDE"), capacity: 4),
            S7ErrorCode.StringCapacityInvalid);
    }

    [Fact]
    public void ValueTypeMismatch_IsRejected()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBD0", S7DataType.Real, S7Value.FromDouble(1.5)),
            S7ErrorCode.ValueTypeMismatch);

        AssertInvalid(
            () => ValidateWrite("DB1.DBX0.3", S7DataType.Bool, S7Value.FromInt16(1)),
            S7ErrorCode.ValueTypeMismatch);

        AssertInvalid(
            () => ValidateWrite("DB1.DBW0", S7DataType.Int, S7Value.FromSingle(1.5f)),
            S7ErrorCode.ValueTypeMismatch);

        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.String, S7Value.FromByte(1), capacity: 8),
            S7ErrorCode.ValueTypeMismatch);
    }

    [Fact]
    public void InputAreaWrite_IsRejected()
    {
        AssertInvalid(
            () => ValidateWrite("IB0", S7DataType.Byte, S7Value.FromByte(1)),
            S7ErrorCode.AreaNotSupported);
    }

    [Fact]
    public void OccupiedRangeOverflow_IsRejected()
    {
        AssertInvalid(
            () => ValidateRead("DB1.DBD65534", S7DataType.Real),
            S7ErrorCode.AddressOutOfRange);
    }

    [Fact]
    public void StringOverflow_IsRejected()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBB0", S7DataType.String, S7Value.FromString("ABCDE"), capacity: 4),
            S7ErrorCode.StringCapacityInvalid);
    }

    [Fact]
    public void WidenIntegerFactories_AreAcceptedWithinRange()
    {
        Assert.Equal(1, ValidateWrite("DB1.DBB0", S7DataType.Byte, S7Value.FromInt32(200)));
        Assert.Equal(2, ValidateWrite("DB1.DBW0", S7DataType.Word, S7Value.FromInt64(60000)));
        Assert.Equal(4, ValidateWrite("DB1.DBD0", S7DataType.DWord, S7Value.FromUInt64(4000000000)));
        Assert.Equal(4, ValidateWrite("DB1.DBD0", S7DataType.DInt, S7Value.FromInt64(-2000000000)));
    }

    [Fact]
    public void ValidRequests_ReturnOccupiedBytes()
    {
        Assert.Equal(2, ValidateRead("DB1.DBW0", S7DataType.Int));
        Assert.Equal(1, ValidateRead("DB1.DBX0.3", S7DataType.Bool));
        Assert.Equal(8, ValidateWrite("DB2.DBD0", S7DataType.LReal, S7Value.FromDouble(2.5)));
        Assert.Equal(1, ValidateWrite("DB2.DBX0.3", S7DataType.Bool, S7Value.FromBool(true)));
        Assert.Equal(18, ValidateWrite("DB2.DBB16", S7DataType.S7String, S7Value.FromString("S7-PLC"), capacity: 16));
        Assert.Equal(8, ValidateWrite("DB2.DBB40", S7DataType.String, S7Value.FromString("HELLO"), capacity: 8));
    }

    [Fact]
    public void ErrorMessage_DoesNotContainWriteValue()
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => ValidateWrite("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(12345)));

        Assert.Equal(S7ErrorCode.ValueOutOfRange, exception.ErrorCode);
        Assert.DoesNotContain("12345", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WString_OccupiedBytes_MatchRules()
    {
        Assert.Equal(36, ValidateRead("DB2.DBB64", S7DataType.S7WString, 16));
        Assert.Equal(504, ValidateRead("DB2.DBB8448", S7DataType.S7WString, 250));
        Assert.Equal(32770, ValidateRead("DB2.DBB0", S7DataType.S7WString, 16383));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16384)]
    public void WString_InvalidCapacity_IsRejected(int capacity)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => ValidateRead("DB2.DBB0", S7DataType.S7WString, capacity));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
    }

    [Theory]
    [InlineData("DB2.DBW64")]
    [InlineData("DB2.DBD64")]
    [InlineData("DB2.DBX64.0")]
    public void WString_WrongWidth_IsRejected(string expression)
    {
        AssertInvalid(
            () => ValidateRead(expression, S7DataType.S7WString, 16),
            S7ErrorCode.DataTypeAddressMismatch);
    }

    [Fact]
    public void WString_OccupiedRangeOverflow_IsRejected()
    {
        AssertInvalid(
            () => ValidateRead("DB2.DBB65500", S7DataType.S7WString, 100),
            S7ErrorCode.AddressOutOfRange);
    }

    [Fact]
    public void WString_ContentAndSurrogateRules()
    {
        AssertInvalid(
            () => ValidateWrite(
                "DB2.DBB64",
                S7DataType.S7WString,
                S7Value.FromString("ABCDE"),
                4),
            S7ErrorCode.StringCapacityInvalid);

        AssertInvalid(
            () => ValidateWrite(
                "DB2.DBB64",
                S7DataType.S7WString,
                S7Value.FromString("\uD83D"),
                16),
            S7ErrorCode.StringEncodingInvalid);

        AssertInvalid(
            () => ValidateWrite(
                "IB0",
                S7DataType.S7WString,
                S7Value.FromString("A"),
                8),
            S7ErrorCode.AreaNotSupported);
    }

    [Fact]
    public void WString_ValidCapacity16383_IsAccepted()
    {
        Assert.Equal(
            32770,
            ValidateWrite(
                "DB2.DBB0",
                S7DataType.S7WString,
                S7Value.FromString("A"),
                16383));
    }

    [Fact]
    public void WString_ErrorMessages_DoNotContainContent()
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => ValidateWrite(
                "DB2.DBB64",
                S7DataType.S7WString,
                S7Value.FromString("温度ABCDE"),
                4));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.DoesNotContain("温度", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDE", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB1.DBR13.15", S7DataType.String, 15, S7AddressSuffix.R)]
    [InlineData("DB1.DBS13.15", S7DataType.S7String, 17, S7AddressSuffix.S)]
    [InlineData("DB1.DBL13.15", S7DataType.S7WString, 34, S7AddressSuffix.L)]
    public void CapacitySuffix_WithMatchingType_UsesAddressCapacity(
        string expression,
        S7DataType dataType,
        int expectedBytes,
        S7AddressSuffix expectedSuffix)
    {
        TypedValidationResult result = ValidateReadResult(expression, dataType);

        Assert.Equal(dataType, result.DataType);
        Assert.Equal(expectedSuffix, result.Suffix);
        Assert.Equal(15, result.AddressCapacity);
        Assert.Equal(15, result.EffectiveCapacity);
        Assert.Equal(expectedBytes, result.BytesOccupied);
        Assert.Equal(expectedBytes, ValidateRead(expression, dataType));
    }

    [Theory]
    [InlineData("DB1.DBR13.15", S7DataType.S7String, S7AddressSuffix.R)]
    [InlineData("DB1.DBR13.15", S7DataType.Byte, S7AddressSuffix.R)]
    [InlineData("DB1.DBR13.15", S7DataType.S7WString, S7AddressSuffix.R)]
    [InlineData("DB1.DBS13.15", S7DataType.String, S7AddressSuffix.S)]
    [InlineData("DB1.DBL13.15", S7DataType.S7String, S7AddressSuffix.L)]
    [InlineData("DB1.DBL13.15", S7DataType.String, S7AddressSuffix.L)]
    public void CapacitySuffix_WithDifferentType_Reports133(
        string expression,
        S7DataType dataType,
        S7AddressSuffix expectedSuffix)
    {
        S7ClientException exception = AssertInvalid(
            () => ValidateRead(expression, dataType),
            S7ErrorCode.DataTypeAddressMismatch);

        Assert.Contains($"后缀 {expectedSuffix}", exception.Message, StringComparison.Ordinal);
        Assert.Contains(dataType.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB1.DBR13.15")]
    [InlineData("DB1.DBS13.15")]
    [InlineData("DB1.DBL13.15")]
    public void CapacitySuffix_WithoutExplicitType_Reports133(string expression)
    {
        S7ClientException exception = AssertInvalid(
            () => ValidateRead(expression, default),
            S7ErrorCode.DataTypeAddressMismatch);

        Assert.Contains("必须显式提供数据类型", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB1.DBR13.15", S7DataType.String, 16)]
    [InlineData("DB1.DBS13.15", S7DataType.S7String, 8)]
    [InlineData("DB1.DBL13.15", S7DataType.S7WString, 16)]
    public void CapacitySuffix_ConflictingCapacityParameter_Reports136(
        string expression,
        S7DataType dataType,
        int capacity)
    {
        S7ClientException exception = AssertInvalid(
            () => ValidateRead(expression, dataType, capacity),
            S7ErrorCode.StringCapacityInvalid);

        Assert.Contains("不一致", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"容量参数 {capacity}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CapacitySuffix_MatchingCapacityParameter_IsAccepted()
    {
        Assert.Equal(15, ValidateRead("DB1.DBR13.15", S7DataType.String, capacity: 15));
        Assert.Equal(
            34,
            ValidateWrite(
                "DB1.DBL13.15",
                S7DataType.S7WString,
                S7Value.FromString("A"),
                capacity: 15));
    }

    [Fact]
    public void CapacitySuffix_ContentLength_RespectsAddressCapacity()
    {
        Assert.Equal(
            15,
            ValidateWrite("DB1.DBR13.15", S7DataType.String, S7Value.FromString(new string('A', 14))));

        AssertInvalid(
            () => ValidateWrite("DB1.DBR13.15", S7DataType.String, S7Value.FromString(new string('A', 15))),
            S7ErrorCode.StringCapacityInvalid);

        Assert.Equal(
            17,
            ValidateWrite("DB1.DBS13.15", S7DataType.S7String, S7Value.FromString(new string('A', 15))));

        AssertInvalid(
            () => ValidateWrite("DB1.DBS13.15", S7DataType.S7String, S7Value.FromString(new string('A', 16))),
            S7ErrorCode.StringCapacityInvalid);

        Assert.Equal(
            34,
            ValidateWrite("DB1.DBL13.15", S7DataType.S7WString, S7Value.FromString(new string('A', 15))));

        AssertInvalid(
            () => ValidateWrite("DB1.DBL13.15", S7DataType.S7WString, S7Value.FromString(new string('A', 16))),
            S7ErrorCode.StringCapacityInvalid);

        AssertInvalid(
            () => ValidateWrite("DB1.DBL13.15", S7DataType.S7WString, S7Value.FromString("\uD83D")),
            S7ErrorCode.StringEncodingInvalid);
    }

    [Theory]
    [InlineData("DB1.DBB0", S7DataType.Byte)]
    [InlineData("DB1.DBW0", S7DataType.Word)]
    [InlineData("DB1.DBD0", S7DataType.DWord)]
    [InlineData("DB1.DBD0", S7DataType.DInt)]
    [InlineData("DB1.DBD0", S7DataType.LReal)]
    public void StringValue_ForNonStringType_ReportsMismatch(string expression, S7DataType dataType)
    {
        AssertInvalid(
            () => ValidateWrite(expression, dataType, S7Value.FromString("A")),
            S7ErrorCode.ValueTypeMismatch);
    }

    [Fact]
    public void NonStringValue_ForWString_ReportsMismatch()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBL13.15", S7DataType.S7WString, S7Value.FromByte(1)),
            S7ErrorCode.ValueTypeMismatch);
    }

    [Fact]
    public void DIntRangeViolation_IsRejected()
    {
        AssertInvalid(
            () => ValidateWrite("DB1.DBD0", S7DataType.DInt, S7Value.FromInt64(long.MinValue)),
            S7ErrorCode.ValueOutOfRange);
    }

    [Fact]
    public void UnsignedValue_ForSignedType_WithinRange_IsAccepted()
    {
        Assert.Equal(2, ValidateWrite("DB1.DBW0", S7DataType.Int, S7Value.FromUInt16(100)));
    }

    [Fact]
    public void ByteSuffix_WithCapacityParameter_KeepsOriginalBehavior()
    {
        TypedValidationResult result = ValidateReadResult("DB1.DBB13", S7DataType.String, 15);

        Assert.Equal(S7AddressSuffix.B, result.Suffix);
        Assert.Null(result.AddressCapacity);
        Assert.Equal(15, result.EffectiveCapacity);
        Assert.Equal(15, result.BytesOccupied);
    }

    [Fact]
    public void NonDataBlockAddress_WithCapacityParameter_KeepsOriginalBehavior()
    {
        TypedValidationResult result = ValidateReadResult("MB10", S7DataType.String, 8);

        Assert.Equal(S7AddressSuffix.None, result.Suffix);
        Assert.Null(result.AddressCapacity);
        Assert.Equal(8, result.EffectiveCapacity);
        Assert.Equal(8, result.BytesOccupied);
    }

    private static S7ClientException AssertInvalid(Action action, S7ErrorCode expectedCode)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(action);

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
        return exception;
    }

    private static int ValidateRead(string expression, S7DataType dataType, int capacity = 0)
    {
        return ValidateReadResult(expression, dataType, capacity).BytesOccupied;
    }

    private static TypedValidationResult ValidateReadResult(
        string expression,
        S7DataType dataType,
        int capacity = 0)
    {
        S7Address address = S7Address.Parse(expression);
        var request = new S7TypedReadRequest(expression, dataType, capacity);
        return TypedRequestValidator.ValidateRead(request, address);
    }

    private static int ValidateWrite(
        string expression,
        S7DataType dataType,
        S7Value value,
        int capacity = 0)
    {
        S7Address address = S7Address.Parse(expression);
        var request = new S7TypedWriteRequest(expression, dataType, value, capacity);
        return TypedRequestValidator.ValidateWrite(request, address).BytesOccupied;
    }

    }