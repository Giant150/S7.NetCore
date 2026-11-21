using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcSerialCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedReadTests : VirtualPlcTestBase
{
    [VirtualPlcFact]
    public async Task T_R01_Db1Word_MultipleViewsAgree()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7TypedReadResult intResult = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Int),
            BudgetToken);
        S7TypedReadResult wordResult = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBW0", S7DataType.Word),
            BudgetToken);

        short intValue = intResult.Value.AsInt16();
        ushort wordValue = wordResult.Value.AsUInt16();

        Assert.Equal(unchecked((ushort)intValue), wordValue);
        Assert.Equal(intValue, wordValue >= 32768 ? wordValue - 65536 : wordValue);
        Assert.Equal(2, intResult.BytesOccupied);
        Assert.Empty(ChunkFrameInspector.WriteChunks(session.Transport.Frames));
    }

    [VirtualPlcFact]
    public async Task T_R02_Db1DWord_MultipleViewsAgree()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        S7TypedReadResult dintResult = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBD0", S7DataType.DInt),
            BudgetToken);
        S7TypedReadResult dwordResult = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBD0", S7DataType.DWord),
            BudgetToken);
        S7TypedReadResult realResult = await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBD0", S7DataType.Real),
            BudgetToken);

        int dintValue = dintResult.Value.AsInt32();
        uint dwordValue = dwordResult.Value.AsUInt32();
        uint realBits = BitConverter.SingleToUInt32Bits(realResult.Value.AsSingle());

        Assert.Equal(unchecked((int)dwordValue), dintValue);
        Assert.Equal(dwordValue, realBits);
        Assert.Equal(4, dintResult.BytesOccupied);
        Assert.Equal(4, realResult.BytesOccupied);
        Assert.Empty(ChunkFrameInspector.WriteChunks(session.Transport.Frames));
    }

    [VirtualPlcFact]
    public async Task T_R03_Db1BitAndByte_Agree()
    {
        await using VirtualPlcSession session = CreateSession();
        await session.Client.ConnectAsync(BudgetToken);

        bool bitValue = (await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBX0.3", S7DataType.Bool),
            BudgetToken)).Value.AsBoolean();

        byte byteValue = (await session.Client.ReadValueAsync(
            new S7TypedReadRequest("DB1.DBB0", S7DataType.Byte),
            BudgetToken)).Value.AsByte();

        Assert.Equal(((byteValue >> 3) & 1) == 1, bitValue);
    }
}