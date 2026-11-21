using S7.NetCore.Tests.VirtualPlc.Server;

namespace S7.NetCore.Tests.VirtualPlc;

/// <summary>
/// 真实虚拟 PLC 用例串行集合：同一虚拟 PLC 上的写用例不得并发执行。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VirtualPlcSerialCollection : ICollectionFixture<VirtualPlcServerFixture>
{
    /// <summary>集合名称。</summary>
    public const string Name = "VirtualPlcSerial";
}