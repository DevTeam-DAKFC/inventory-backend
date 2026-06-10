using Inventory.Api.Models;

namespace Inventory.Api.Contracts.Imports;

public record ImportBatchResponse(
    Guid Id,
    string FileName,
    Guid ImportedBy,
    string Status,
    int TotalRows,
    int ProcessedRows,
    int ImportedRows,
    int FailedRows,
    DateTime CreatedAt,
    DateTime? CompletedAt)
{
    public static ImportBatchResponse FromEntity(ImportBatch batch) =>
        new(
            batch.Id,
            batch.FileName,
            batch.ImportedById,
            ToContractStatus(batch),
            batch.TotalRows,
            batch.TotalRows,
            batch.ValidRows,
            batch.InvalidRows,
            batch.CreatedAt,
            batch.CompletedAt);

    private static string ToContractStatus(ImportBatch batch)
    {
        if (batch.Status == ImportStatus.Completed && batch.InvalidRows > 0)
        {
            return "completed_with_errors";
        }

        return batch.Status switch
        {
            ImportStatus.Pending => "pending",
            ImportStatus.Validated => "processing",
            ImportStatus.Completed => "completed",
            ImportStatus.Failed => "failed",
            _ => batch.Status.ToString()
        };
    }
}
