using Inventory.Api.Contracts.Common;

namespace Inventory.Api.Contracts.Products;

public record ProductUpdateRequest(
    PatchField<string> Name,
    PatchField<string> Sku,
    PatchField<string> Barcode,
    PatchField<string> Category,
    PatchField<string> Description,
    PatchField<string> ImageUrl,
    PatchField<int> MinStock)
{
    public bool HasAnyUpdatableField =>
        Name.IsPresent ||
        Sku.IsPresent ||
        Barcode.IsPresent ||
        Category.IsPresent ||
        Description.IsPresent ||
        ImageUrl.IsPresent ||
        MinStock.IsPresent;
}
