namespace Inventory.Api.Dtos;

public record ErrorResponse(
    string Code,
    string Message,
    IDictionary<string, string[]>? Errors = null);
