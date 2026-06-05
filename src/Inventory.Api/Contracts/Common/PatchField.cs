namespace Inventory.Api.Contracts.Common;

public readonly record struct PatchField<T>(bool IsPresent, T? Value)
{
    public static PatchField<T> Missing => new(false, default);

    public static PatchField<T> Present(T? value) => new(true, value);
}
