using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedZeroSendTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task T_Z01_InvalidAddresses_AreRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW", S7DataType.Int), BudgetToken),
            S7ErrorCode.AddressSyntaxInvalid);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBX0.8", S7DataType.Bool), BudgetToken),
            S7ErrorCode.BitNumberOutOfRange);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB0.DBW0", S7DataType.Word), BudgetToken),
            S7ErrorCode.AddressOutOfRange);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("MW65536", S7DataType.Word), BudgetToken),
            S7ErrorCode.AddressOutOfRange);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("T1", S7DataType.Word), BudgetToken),
            S7ErrorCode.AreaNotSupported);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBB0.0", S7DataType.Byte), BudgetToken),
            S7ErrorCode.BitNumberOutOfRange);
    }

    [VirtualPlcFact]
    public async Task T_Z02_TypeWidthMismatch_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBB0", S7DataType.Word), BudgetToken),
            S7ErrorCode.DataTypeAddressMismatch);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBW0", S7DataType.Real), BudgetToken),
            S7ErrorCode.DataTypeAddressMismatch);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(new S7TypedReadRequest("DB1.DBD0", S7DataType.Word), BudgetToken),
            S7ErrorCode.DataTypeAddressMismatch);
    }

    [VirtualPlcFact]
    public async Task T_Z03_ValueOutOfRange_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(256)),
                BudgetToken),
            S7ErrorCode.ValueOutOfRange);

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB1.DBB0", S7DataType.Byte, S7Value.FromInt16(-1)),
                BudgetToken),
            S7ErrorCode.ValueOutOfRange);

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest("DB1.DBW0", S7DataType.Word, S7Value.FromInt32(65536)),
                BudgetToken),
            S7ErrorCode.ValueOutOfRange);
    }

    [VirtualPlcFact]
    public async Task T_Z04_NonAsciiString_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB1.DBB0",
                    S7DataType.String,
                    S7Value.FromString("PLČ"),
                    Capacity: 8),
                BudgetToken),
            S7ErrorCode.StringEncodingInvalid);
    }

    [VirtualPlcFact]
    public async Task T_Z05_StringCapacityOverflow_IsRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB1.DBB0",
                    S7DataType.String,
                    S7Value.FromString("ABCDE"),
                    Capacity: 4),
                BudgetToken),
            S7ErrorCode.StringCapacityInvalid);
    }

    [VirtualPlcFact]
    public async Task T_Z06_WStringInvalidInputs_AreRejectedWithoutTraffic()
    {
        await using VirtualPlcSession session = await ConnectAsync();

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB64", S7DataType.S7WString, 0),
                BudgetToken),
            S7ErrorCode.StringCapacityInvalid);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB64", S7DataType.S7WString, 16384),
                BudgetToken),
            S7ErrorCode.StringCapacityInvalid);

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBB64",
                    S7DataType.S7WString,
                    S7Value.FromString("ABCDE"),
                    4),
                BudgetToken),
            S7ErrorCode.StringCapacityInvalid);

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "DB2.DBB64",
                    S7DataType.S7WString,
                    S7Value.FromString("\uD83D"),
                    16),
                BudgetToken),
            S7ErrorCode.StringEncodingInvalid);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBW64", S7DataType.S7WString, 16),
                BudgetToken),
            S7ErrorCode.DataTypeAddressMismatch);

        await AssertZeroSendAsync(
            session,
            () => session.Client.WriteValueAsync(
                new S7TypedWriteRequest(
                    "IB0",
                    S7DataType.S7WString,
                    S7Value.FromString("A"),
                    8),
                BudgetToken),
            S7ErrorCode.AreaNotSupported);

        await AssertZeroSendAsync(
            session,
            () => session.Client.ReadValueAsync(
                new S7TypedReadRequest("DB2.DBB65500", S7DataType.S7WString, 100),
                BudgetToken),
            S7ErrorCode.AddressOutOfRange);
    }

    private async Task<VirtualPlcSession> ConnectAsync()
    {
        VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);
        return session;
    }

    private static async Task AssertZeroSendAsync(
        VirtualPlcSession session,
        Func<Task> action,
        S7ErrorCode expectedCode)
    {
        int writeCountBefore = session.Transport.WriteCount;
        S7ClientException exception = await Assert.ThrowsAsync<S7ClientException>(action);

        Assert.Equal(S7ErrorCategory.InvalidArgument, exception.Category);
        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(writeCountBefore, session.Transport.WriteCount);
    }
}