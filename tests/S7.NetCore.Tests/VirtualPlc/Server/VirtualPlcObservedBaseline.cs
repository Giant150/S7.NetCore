namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed record VirtualPlcObservedBaseline(
    int NegotiatedPduSize,
    int ReadChunkSize,
    int WriteChunkSize,
    byte OutOfRangeReturnCode,
    byte ObjectNotExistReturnCode,
    double StartupMilliseconds);