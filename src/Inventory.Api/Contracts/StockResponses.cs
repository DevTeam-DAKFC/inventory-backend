namespace Inventory.Api.Contracts;

public sealed record StockResponse(
    Guid Id,
    int AvailableQuantity,
    int MinStock,
    bool IsLowStock,
    DateTime? LastMovementAt,
    DateTime? UpdatedAt,
    StockProductResponse Product,
    StockBranchResponse Branch);

public sealed record StockProductResponse(
    Guid Id,
    string Name,
    string Sku,
    string? Barcode,
    string Category,
    string? ImageUrl);

public sealed record StockBranchResponse(
    Guid Id,
    string Name,
    string? Address);
