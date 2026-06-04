namespace Inventory.Api.Models;

public class ImportBatchError
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public ImportBatch ImportBatch { get; set; } = null!;
}
