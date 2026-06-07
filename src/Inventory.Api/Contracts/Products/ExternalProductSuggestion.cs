namespace Inventory.Api.Contracts.Products;

public record ExternalProductSuggestion(
    string Barcode,
    string? Name,
    string? Brand,
    string? Category,
    string? ImageUrl,
    string Source);
