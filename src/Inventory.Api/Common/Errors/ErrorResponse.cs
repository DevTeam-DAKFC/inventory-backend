namespace Inventory.Api.Common.Errors;

public class ErrorResponse
{
    public required ErrorBody Error { get; init; }
}
