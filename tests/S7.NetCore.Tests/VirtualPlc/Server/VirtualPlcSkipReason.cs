namespace S7.NetCore.Tests.VirtualPlc.Server;

/// <summary>
/// 环境层稳定跳过原因码。
/// </summary>
internal enum VirtualPlcSkipReason
{
    ModeOff,
    LibraryMissing,
    LibraryBadImage,
    LibraryEntryPointMissing,
    PortInvalid,
    PortInUse,
    ExternalEndpointUnreachable,
    ConfigurationInvalid
}