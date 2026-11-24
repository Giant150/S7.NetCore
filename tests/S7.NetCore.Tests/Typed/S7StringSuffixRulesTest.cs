using S7.NetCore;
using S7.NetCore.Internal;

namespace S7.NetCore.Tests.Typed;

public sealed class S7StringSuffixRulesTest
{
    [Theory]
    [InlineData(S7AddressSuffix.R, S7DataType.String)]
    [InlineData(S7AddressSuffix.S, S7DataType.S7String)]
    [InlineData(S7AddressSuffix.L, S7DataType.S7WString)]
    public void TryGetAllowedDataType_StringCapacitySuffix_MapsToExpectedType(
        S7AddressSuffix suffix,
        S7DataType expected)
    {
        bool found = S7StringSuffixRules.TryGetAllowedDataType(suffix, out S7DataType dataType);

        Assert.True(found);
        Assert.Equal(expected, dataType);
        Assert.True(S7StringSuffixRules.IsCapacitySuffix(suffix));
    }

    [Theory]
    [InlineData(S7AddressSuffix.None)]
    [InlineData(S7AddressSuffix.B)]
    [InlineData(S7AddressSuffix.W)]
    [InlineData(S7AddressSuffix.D)]
    [InlineData(S7AddressSuffix.X)]
    public void TryGetAllowedDataType_NonCapacitySuffix_ReturnsFalse(S7AddressSuffix suffix)
    {
        bool found = S7StringSuffixRules.TryGetAllowedDataType(suffix, out S7DataType dataType);

        Assert.False(found);
        Assert.Equal(default(S7DataType), dataType);
        Assert.False(S7StringSuffixRules.IsCapacitySuffix(suffix));
    }

    [Theory]
    [InlineData(S7AddressSuffix.R, 1)]
    [InlineData(S7AddressSuffix.R, 254)]
    [InlineData(S7AddressSuffix.S, 1)]
    [InlineData(S7AddressSuffix.S, 254)]
    [InlineData(S7AddressSuffix.L, 1)]
    [InlineData(S7AddressSuffix.L, 16383)]
    public void ValidateAddressCapacity_WithinLimits_DoesNotThrow(S7AddressSuffix suffix, int capacity)
    {
        S7StringSuffixRules.ValidateAddressCapacity(suffix, capacity, "DB1.DBR13.15");
    }

    [Theory]
    [InlineData(S7AddressSuffix.R, 0)]
    [InlineData(S7AddressSuffix.R, 255)]
    [InlineData(S7AddressSuffix.S, 0)]
    [InlineData(S7AddressSuffix.S, 255)]
    [InlineData(S7AddressSuffix.L, 0)]
    [InlineData(S7AddressSuffix.L, 16384)]
    public void ValidateAddressCapacity_OutOfLimits_Reports136(S7AddressSuffix suffix, int capacity)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7StringSuffixRules.ValidateAddressCapacity(suffix, capacity, "DB1.DB"));

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
        Assert.Contains("容量必须为", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"容量 {capacity}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(S7AddressSuffix.None)]
    [InlineData(S7AddressSuffix.B)]
    [InlineData(S7AddressSuffix.W)]
    [InlineData(S7AddressSuffix.D)]
    [InlineData(S7AddressSuffix.X)]
    public void ValidateAddressCapacity_NonCapacitySuffix_Reports136(S7AddressSuffix suffix)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => S7StringSuffixRules.ValidateAddressCapacity(suffix, 8, null));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.Contains("后缀不是字符串容量后缀", exception.Message, StringComparison.Ordinal);
        Assert.Contains("<null>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveEffectiveCapacity_WithoutAddressCapacity_ReturnsParameter()
    {
        S7Address address = S7AddressParser.ParseSyntax("DB1.DBB0");

        Assert.Null(address.AddressCapacity);
        Assert.Equal(0, Resolve(address, 0));
        Assert.Equal(8, Resolve(address, 8));
    }

    [Theory]
    [InlineData("DB1.DBR13.15", 0, 15)]
    [InlineData("DB1.DBR13.15", 15, 15)]
    [InlineData("DB1.DBS13.254", 0, 254)]
    [InlineData("DB1.DBL13.16383", 0, 16383)]
    public void ResolveEffectiveCapacity_WithAddressCapacity_PrefersAddressValue(
        string expression,
        int capacityParameter,
        int expected)
    {
        S7Address address = S7AddressParser.ParseSyntax(expression);

        Assert.Equal(expected, address.AddressCapacity);
        Assert.Equal(expected, Resolve(address, capacityParameter));
    }

    [Theory]
    [InlineData("DB1.DBR13.15", 16)]
    [InlineData("DB1.DBS13.15", 14)]
    [InlineData("DB1.DBL13.15", 30)]
    public void ResolveEffectiveCapacity_ConflictingParameter_Reports136(
        string expression,
        int capacityParameter)
    {
        S7Address address = S7AddressParser.ParseSyntax(expression);

        S7ClientException exception = Assert.Throws<S7ClientException>(
            () => Resolve(address, capacityParameter));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.Contains("不一致", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"容量参数 {capacityParameter}", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"地址容量 {address.AddressCapacity}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB1.DBR13.0")]
    [InlineData("DB1.DBR13.255")]
    [InlineData("DB1.DBS13.255")]
    [InlineData("DB1.DBL13.16384")]
    public void ResolveEffectiveCapacity_InvalidAddressCapacity_Reports136(string expression)
    {
        S7Address address = S7AddressParser.ParseSyntax(expression);

        S7ClientException exception = Assert.Throws<S7ClientException>(() => Resolve(address, 0));

        Assert.Equal(S7ErrorCode.StringCapacityInvalid, exception.ErrorCode);
        Assert.Contains("容量必须为", exception.Message, StringComparison.Ordinal);
    }

    private static int Resolve(S7Address address, int capacity)
    {
        return S7StringSuffixRules.ResolveEffectiveCapacity(in address, capacity);
    }
}