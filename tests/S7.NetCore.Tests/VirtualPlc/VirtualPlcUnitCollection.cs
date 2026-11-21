namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 纯单元虚拟 PLC 用例集合：不启动服务器、不绑定端口。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VirtualPlcUnitCollection
{
    /// <summary>集合名称。</summary>
    public const string Name = "VirtualPlcUnit";
}