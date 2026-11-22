using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Validation;

public readonly record struct TypedValidationResult(
    bool IsValid,
    S7Address Address,
    FieldValidationError? Error,
    int OccupiedBytes);

public static class TypedInputValidator
{
    public static TypedValidationResult Validate(TypedAddressSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Expression) || spec.Expression.Length > 64)
        {
            return Invalid(S7ErrorCode.AddressSyntaxInvalid, "地址不能为空且长度不得超过 64 字符。");
        }

        S7Address address;
        try
        {
            address = S7Address.Parse(spec.Expression);
        }
        catch (S7ClientException exception)
        {
            return new TypedValidationResult(false, default, new FieldValidationError("Address", exception.ErrorCode, exception.Message), 0);
        }

        if (!IsCompatible(address.Width, spec.DataType))
        {
            return new TypedValidationResult(
                false,
                address,
                new FieldValidationError("DataType", S7ErrorCode.DataTypeAddressMismatch, "数据类型与地址访问宽度不兼容。"),
                0);
        }

        int occupiedBytes = GetOccupiedBytes(spec.DataType, spec.Capacity);
        if (occupiedBytes <= 0)
        {
            return new TypedValidationResult(
                false,
                address,
                new FieldValidationError("Capacity", S7ErrorCode.StringCapacityInvalid, "字符串容量非法。"),
                0);
        }

        if (spec.DataType is S7DataType.String or S7DataType.S7String or S7DataType.S7WString)
        {
            int maximum = spec.DataType == S7DataType.S7WString ? 16383 : 254;
            if (spec.Capacity < 1 || spec.Capacity > maximum)
            {
                return new TypedValidationResult(
                    false,
                    address,
                    new FieldValidationError("Capacity", S7ErrorCode.StringCapacityInvalid, $"容量必须在 1 至 {maximum} 之间。"),
                    occupiedBytes);
            }
        }
        else if (spec.Capacity != 0)
        {
            return new TypedValidationResult(
                false,
                address,
                new FieldValidationError("Capacity", S7ErrorCode.StringCapacityInvalid, "非字符串类型容量必须为 0。"),
                occupiedBytes);
        }

        if (address.StartByte + occupiedBytes > 65535)
        {
            return new TypedValidationResult(
                false,
                address,
                new FieldValidationError("Address", S7ErrorCode.AddressOutOfRange, "地址与占用长度超出区域容量。"),
                occupiedBytes);
        }

        return new TypedValidationResult(true, address, null, occupiedBytes);
    }

    public static int GetOccupiedBytes(S7DataType dataType, int capacity)
    {
        return dataType switch
        {
            S7DataType.Bool or S7DataType.Byte => 1,
            S7DataType.Word or S7DataType.Int => 2,
            S7DataType.DWord or S7DataType.DInt or S7DataType.Real => 4,
            S7DataType.LReal => 8,
            S7DataType.String => capacity,
            S7DataType.S7String => capacity > 0 ? capacity + 2 : 0,
            S7DataType.S7WString => capacity > 0 ? 4 + (capacity * 2) : 0,
            _ => 0
        };
    }

    private static bool IsCompatible(S7AccessWidth width, S7DataType dataType)
    {
        return width switch
        {
            S7AccessWidth.Bit => dataType == S7DataType.Bool,
            S7AccessWidth.Byte => dataType is S7DataType.Byte or S7DataType.String or S7DataType.S7String or S7DataType.S7WString,
            S7AccessWidth.Word => dataType is S7DataType.Word or S7DataType.Int,
            S7AccessWidth.DWord => dataType is S7DataType.DWord or S7DataType.DInt or S7DataType.Real or S7DataType.LReal,
            _ => false
        };
    }

    private static TypedValidationResult Invalid(S7ErrorCode code, string message)
    {
        return new TypedValidationResult(false, default, new FieldValidationError("Address", code, message), 0);
    }
}