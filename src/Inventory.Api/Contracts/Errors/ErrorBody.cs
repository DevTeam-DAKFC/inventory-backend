namespace Inventory.Api.Contracts.Errors;

public record ErrorBody(
    string Code,
    string Message,
    IReadOnlyList<FieldError> Details,
    string RequestId);
