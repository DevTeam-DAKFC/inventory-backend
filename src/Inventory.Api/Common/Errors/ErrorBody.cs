namespace Inventory.Api.Common.Errors;

public class ErrorBody
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<FieldError>? Details { get; init; }
    public string? RequestId { get; init; }
}
