using S7.NetCore;

namespace S7.NetCore.Tests.Typed;

public sealed class TypedContractTests
{
    [Fact]
    public void DataTypeValues_AreStable()
    {
        Assert.Equal(1, (int)S7DataType.Bool);
        Assert.Equal(2, (int)S7DataType.Byte);
        Assert.Equal(3, (int)S7DataType.Word);
        Assert.Equal(4, (int)S7DataType.Int);
        Assert.Equal(5, (int)S7DataType.DWord);
        Assert.Equal(6, (int)S7DataType.DInt);
        Assert.Equal(7, (int)S7DataType.Real);
        Assert.Equal(8, (int)S7DataType.LReal);
        Assert.Equal(9, (int)S7DataType.String);
        Assert.Equal(10, (int)S7DataType.S7String);
        Assert.Equal(11, (int)S7DataType.S7WString);
    }

    [Fact]
    public void AccessWidthValues_AreStable()
    {
        Assert.Equal(0, (int)S7AccessWidth.Bit);
        Assert.Equal(1, (int)S7AccessWidth.Byte);
        Assert.Equal(2, (int)S7AccessWidth.Word);
        Assert.Equal(3, (int)S7AccessWidth.DWord);
    }

    [Fact]
    public void ErrorCodeValues_AreStable()
    {
        Assert.Equal(130, (int)S7ErrorCode.AddressSyntaxInvalid);
        Assert.Equal(131, (int)S7ErrorCode.BitNumberOutOfRange);
        Assert.Equal(132, (int)S7ErrorCode.DataTypeNotSupported);
        Assert.Equal(133, (int)S7ErrorCode.DataTypeAddressMismatch);
        Assert.Equal(134, (int)S7ErrorCode.ValueOutOfRange);
        Assert.Equal(135, (int)S7ErrorCode.StringEncodingInvalid);
        Assert.Equal(136, (int)S7ErrorCode.StringCapacityInvalid);
        Assert.Equal(137, (int)S7ErrorCode.AddressOutOfRange);
        Assert.Equal(138, (int)S7ErrorCode.ValueTypeMismatch);
        Assert.Equal(406, (int)S7ErrorCode.InvalidS7StringHeader);
        Assert.Equal(407, (int)S7ErrorCode.NonAsciiStringData);
        Assert.Equal(408, (int)S7ErrorCode.InvalidS7WStringHeader);
        Assert.Equal(409, (int)S7ErrorCode.InvalidS7WStringData);

        Assert.Equal(100, (int)S7ErrorCode.InvalidAddress);
        Assert.Equal(200, (int)S7ErrorCode.SessionNotEstablished);
        Assert.Equal(300, (int)S7ErrorCode.TcpConnectFailed);
        Assert.Equal(400, (int)S7ErrorCode.InvalidSetupResponse);
        Assert.Equal(500, (int)S7ErrorCode.PlcHardwareFault);
        Assert.Equal(603, (int)S7ErrorCode.OperationCancelled);
    }

    [Fact]
    public void S7Value_FactoriesAndGetters_RoundTrip()
    {
        Assert.True(S7Value.FromBool(true).AsBoolean());
        Assert.Equal((byte)7, S7Value.FromByte(7).AsByte());
        Assert.Equal((short)-123, S7Value.FromInt16(-123).AsInt16());
        Assert.Equal((ushort)123, S7Value.FromUInt16(123).AsUInt16());
        Assert.Equal(-123456, S7Value.FromInt32(-123456).AsInt32());
        Assert.Equal(123456u, S7Value.FromUInt32(123456).AsUInt32());
        Assert.Equal(1.5f, S7Value.FromSingle(1.5f).AsSingle());
        Assert.Equal(2.5, S7Value.FromDouble(2.5).AsDouble());
        Assert.Equal("S7", S7Value.FromString("S7").AsString());

        Assert.Equal(S7DataType.Bool, S7Value.FromBool(true).DataType);
        Assert.Equal(S7DataType.Int, S7Value.FromInt16(1).DataType);
        Assert.Equal(S7DataType.Word, S7Value.FromUInt16(1).DataType);
        Assert.Equal(S7DataType.DInt, S7Value.FromInt32(1).DataType);
        Assert.Equal(S7DataType.DWord, S7Value.FromUInt32(1).DataType);
        Assert.Equal(S7DataType.Real, S7Value.FromSingle(1).DataType);
        Assert.Equal(S7DataType.LReal, S7Value.FromDouble(1).DataType);
        Assert.Equal(S7DataType.String, S7Value.FromString("x").DataType);
    }

    [Fact]
    public void S7Value_TypeMismatch_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => S7Value.FromInt16(1).AsByte());
        Assert.Throws<InvalidOperationException>(() => S7Value.FromByte(1).AsInt16());
        Assert.Throws<InvalidOperationException>(() => S7Value.FromSingle(1).AsDouble());
        Assert.Throws<InvalidOperationException>(() => S7Value.FromString("x").AsBoolean());
    }

    [Fact]
    public void S7Value_FromStringNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => S7Value.FromString(null!));
    }

    [Fact]
    public void WithContext_PreservesAllErrorFields()
    {
        var writtenRange = new WrittenRange(10, 5);
        var inner = new InvalidOperationException("inner");
        var original = new S7ClientException(
            S7ErrorCategory.PlcReturn,
            S7ErrorCode.PlcObjectNotExist,
            S7OperationStage.Write,
            "原始消息",
            plcReturnCode: 0x0A,
            bytesWritten: 5,
            writtenRange: writtenRange,
            writeOutcomeUncertain: true,
            innerException: inner);

        S7ClientException context = original.WithContext("追加类型上下文");

        Assert.Equal(S7ErrorCategory.PlcReturn, context.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, context.ErrorCode);
        Assert.Equal(S7OperationStage.Write, context.Stage);
        Assert.Equal((byte)0x0A, context.PlcReturnCode);
        Assert.Equal(5, context.BytesWritten);
        Assert.Equal(writtenRange, context.WrittenRange);
        Assert.True(context.WriteOutcomeUncertain);
        Assert.Same(inner, context.InnerException);
        Assert.Contains("追加类型上下文", context.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedRequestAndResult_AreValueTypesWithExpectedDefaults()
    {
        Assert.True(typeof(S7Address).IsValueType);
        Assert.True(typeof(S7Value).IsValueType);
        Assert.True(typeof(S7TypedReadRequest).IsValueType);
        Assert.True(typeof(S7TypedWriteRequest).IsValueType);
        Assert.True(typeof(S7TypedReadResult).IsValueType);

        Assert.Equal(0, default(S7TypedReadRequest).Capacity);
        Assert.Equal(0, default(S7TypedWriteRequest).Capacity);
    }
}