namespace Inventory.Api.Contracts.Products;

public record ProductCreateRequest(
    string? Name,
    string? Sku,
    string? Barcode,
    string? Category,
    string? Description,
    string? ImageUrl,
    int? MinStock,
    bool? IsActive);
