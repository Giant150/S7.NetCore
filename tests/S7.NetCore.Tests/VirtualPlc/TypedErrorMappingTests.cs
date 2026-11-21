using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedErrorMappingTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task T_E01_Db4Read_ReturnsObjectNotExist()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB4.DBW0", S7DataType.Word),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, exception.ErrorCode);
        Assert.Equal((byte)0x0A, exception.PlcReturnCode);
    }

    [VirtualPlcFact]
    public async Task T_E02_Db1Dbd65534_IsRejectedLocally()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);
        int writeCountBefore = session.Transport.WriteCount;

        // spec 5.9.1.17b 期望 PLC 返回 0x05，但 Real 占用 4 字节且起始 65534 已超出
        // 区域容量；按 design 23.3/23.6 的本地占用范围校验返回 137，不发送报文。
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB1.DBD65534", S7DataType.Real),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(S7ErrorCode.AddressOutOfRange, exception.ErrorCode);
        Assert.Equal(writeCountBefore, session.Transport.WriteCount);
    }

    [VirtualPlcFact]
    public async Task T_E03_Db4WString_ReturnsObjectNotExist()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB4.DBB0", S7DataType.S7WString, 8),
                BudgetToken));

        Assert.Equal(S7ErrorCategory.PlcReturn, exception.Category);
        Assert.Equal(S7ErrorCode.PlcObjectNotExist, exception.ErrorCode);
        Assert.Equal((byte)0x0A, exception.PlcReturnCode);
    }
}