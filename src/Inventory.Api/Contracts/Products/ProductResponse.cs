namespace Inventory.Api.Contracts.Products;

public record ProductResponse(
    Guid Id,
    string Name,
    string Sku,
    string? Barcode,
    string Category,
    string? Description,
    string? ImageUrl,
    int MinStock,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
