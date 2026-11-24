namespace S7.NetCore.Internal;

/// <summary>
/// 类型化读写的统一校验结果：生效类型、有效容量、占用字节与地址容量信息。
/// </summary>
internal readonly record struct TypedValidationResult(
    S7DataType DataType,
    int EffectiveCapacity,
    int BytesOccupied,
    S7AddressSuffix Suffix,
    int? AddressCapacity);