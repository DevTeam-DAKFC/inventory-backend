using Inventory.Api.Models;

namespace Inventory.Api.Contracts.Imports;

public record ImportBatchErrorResponse(
    Guid Id,
    Guid BatchId,
    int RowNumber,
    string Field,
    string Code,
    string Message,
    string? RawValue)
{
    public static ImportBatchErrorResponse FromEntity(ImportBatchError error) =>
        new(
            error.Id,
            error.ImportBatchId,
            error.RowNumber,
            error.Field,
            "validation_error",
            error.Message,
            null);
}
