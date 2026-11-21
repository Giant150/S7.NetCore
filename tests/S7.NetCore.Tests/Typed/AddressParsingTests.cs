using System.Diagnostics;
using S7.NetCore;

namespace S7.NetCore.Tests.Typed;

public sealed class AddressParsingTests
{
    [Fact]
    public void T_A01_DbWord_Parses()
    {
        S7Address address = S7Address.Parse("DB1.DBW0");

        Assert.Equal(S7Area.DataBlock, address.Area);
        Assert.Equal(1, address.DbNumber);
        Assert.Equal(0, address.StartByte);
        Assert.Null(address.BitNumber);
        Assert.Equal(S7AccessWidth.Word, address.Width);
        Assert.Equal("DB1.DBW0", address.Expression);
    }

    [Fact]
    public void T_A02_OutputBit_Parses()
    {
        S7Address address = S7Address.Parse("Q0.3");

        Assert.Equal(S7Area.Output, address.Area);
        Assert.Equal(0, address.StartByte);
        Assert.Equal(3, address.BitNumber);
        Assert.Equal(S7AccessWidth.Bit, address.Width);
    }

    [Fact]
    public void T_A03_TrimsOuterWhitespace_ButRejectsInnerWhitespace()
    {
        S7Address trimmed = S7Address.Parse(" db1.dbw0 ");
        S7Address canonical = S7Address.Parse("DB1.DBW0");

        Assert.Equal(canonical.Area, trimmed.Area);
        Assert.Equal(canonical.DbNumber, trimmed.DbNumber);
        Assert.Equal(canonical.StartByte, trimmed.StartByte);
        Assert.Equal(canonical.BitNumber, trimmed.BitNumber);
        Assert.Equal(canonical.Width, trimmed.Width);
        Assert.Equal("db1.dbw0", trimmed.Expression);

        AssertCode("DB1 . DBW0", S7ErrorCode.AddressSyntaxInvalid);
    }

    [Theory]
    [InlineData("DB0.DBW0", S7ErrorCode.AddressOutOfRange)]
    [InlineData("MW65536", S7ErrorCode.AddressOutOfRange)]
    [InlineData("DB1.DBW-1", S7ErrorCode.AddressSyntaxInvalid)]
    public void T_A04_OutOfRangeOrNegative(string expression, S7ErrorCode expectedCode)
    {
        AssertCode(expression, expectedCode);
    }

    [Theory]
    [InlineData("DB1.DBX0.8")]
    [InlineData("DB1.DBB0.0")]
    [InlineData("DB1.DBX0")]
    [InlineData("DB1.DBW0.3")]
    public void T_A05_BitNumberErrors(string expression)
    {
        AssertCode(expression, S7ErrorCode.BitNumberOutOfRange);
    }

    [Theory]
    [InlineData("DB1.W0")]
    [InlineData("DB1.DB")]
    [InlineData("DB1.DBB")]
    [InlineData("DB.DBW0")]
    [InlineData("IX0.3")]
    [InlineData("MX0.3")]
    [InlineData("IBX0.3")]
    [InlineData("")]
    [InlineData("   ")]
    public void T_A06_SyntaxErrors(string expression)
    {
        AssertCode(expression, S7ErrorCode.AddressSyntaxInvalid);
    }

    [Theory]
    [InlineData("T1")]
    [InlineData("C1")]
    [InlineData("Z1")]
    public void T_A07_TimerCounterAreas_AreRejected(string expression)
    {
        AssertCode(expression, S7ErrorCode.AreaNotSupported);
    }

    [Theory]
    [InlineData("MotorSpeed")]
    [InlineData("K1")]
    [InlineData("X1")]
    public void T_A08_SymbolicNames_AreRejected(string expression)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(() => S7Address.Parse(expression));

        Assert.Equal(S7ErrorCode.AddressSyntaxInvalid, exception.ErrorCode);
        Assert.Contains("符号寻址", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void T_A09_DeterminismAndPerformance()
    {
        S7Address first = S7Address.Parse("DB1.DBW0");

        for (int i = 0; i < 100; i++)
        {
            S7Address current = S7Address.Parse("DB1.DBW0");
            Assert.Equal(first.Area, current.Area);
            Assert.Equal(first.DbNumber, current.DbNumber);
            Assert.Equal(first.StartByte, current.StartByte);
            Assert.Equal(first.BitNumber, current.BitNumber);
            Assert.Equal(first.Width, current.Width);
        }

        for (int i = 0; i < 10; i++)
        {
            _ = S7Address.Parse("DB1.DBW0");
        }

        var samples = new double[1000];
        for (int i = 0; i < samples.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = S7Address.Parse("DB1.DBW0");
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(samples);
        double p95 = samples[(int)Math.Ceiling(0.95 * samples.Length) - 1];

        Assert.True(p95 <= 1.0, $"单次地址解析 P95={p95:F4}ms 超过 1ms。");
        Assert.True(samples[^1] < 10.0, $"单次地址解析最大耗时 {samples[^1]:F4}ms 异常。");
    }

    [Fact]
    public void T_A10_ExpressionOver64Characters_IsRejected()
    {
        string expression = "DB1.DBW" + new string('0', 58);
        Assert.Equal(65, expression.Length);

        S7ClientException exception = Assert.Throws<S7ClientException>(() => S7Address.Parse(expression));

        Assert.Equal(S7ErrorCode.AddressSyntaxInvalid, exception.ErrorCode);
        Assert.Contains("64", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB1.DBB0", S7Area.DataBlock, 1, 0, S7AccessWidth.Byte)]
    [InlineData("DB1.DBD0", S7Area.DataBlock, 1, 0, S7AccessWidth.DWord)]
    [InlineData("DB1.DBX0.7", S7Area.DataBlock, 1, 0, S7AccessWidth.Bit)]
    [InlineData("IB0", S7Area.Input, 0, 0, S7AccessWidth.Byte)]
    [InlineData("IW10", S7Area.Input, 0, 10, S7AccessWidth.Word)]
    [InlineData("ID4", S7Area.Input, 0, 4, S7AccessWidth.DWord)]
    [InlineData("E0.3", S7Area.Input, 0, 0, S7AccessWidth.Bit)]
    [InlineData("QB0", S7Area.Output, 0, 0, S7AccessWidth.Byte)]
    [InlineData("QW0", S7Area.Output, 0, 0, S7AccessWidth.Word)]
    [InlineData("QD0", S7Area.Output, 0, 0, S7AccessWidth.DWord)]
    [InlineData("A0.3", S7Area.Output, 0, 0, S7AccessWidth.Bit)]
    [InlineData("O0.3", S7Area.Output, 0, 0, S7AccessWidth.Bit)]
    [InlineData("MB10", S7Area.Memory, 0, 10, S7AccessWidth.Byte)]
    [InlineData("MW10", S7Area.Memory, 0, 10, S7AccessWidth.Word)]
    [InlineData("MD10", S7Area.Memory, 0, 10, S7AccessWidth.DWord)]
    [InlineData("M10.3", S7Area.Memory, 0, 10, S7AccessWidth.Bit)]
    [InlineData("DB01.DBW00", S7Area.DataBlock, 1, 0, S7AccessWidth.Word)]
    public void AdditionalAddressForms_Parse(
        string expression,
        S7Area area,
        int dbNumber,
        int startByte,
        S7AccessWidth width)
    {
        S7Address address = S7Address.Parse(expression);

        Assert.Equal(area, address.Area);
        Assert.Equal(dbNumber, address.DbNumber);
        Assert.Equal(startByte, address.StartByte);
        Assert.Equal(width, address.Width);
    }

    [Fact]
    public void TryParse_MatchesParse()
    {
        Assert.True(S7Address.TryParse("DB1.DBW0", out S7Address valid));
        Assert.Equal(S7Address.Parse("DB1.DBW0").StartByte, valid.StartByte);

        Assert.False(S7Address.TryParse("T1", out _));
        Assert.False(S7Address.TryParse(null!, out _));
    }

    private static void AssertCode(string expression, S7ErrorCode expectedCode)
    {
        S7ClientException exception = Assert.Throws<S7ClientException>(() => S7Address.Parse(expression));

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(S7OperationStage.Validate, exception.Stage);
    }
}