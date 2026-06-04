namespace Inventory.Api.Models;

public class ImportBatch
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public ImportStatus Status { get; set; }
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public Guid ImportedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public AppUser ImportedBy { get; set; } = null!;
    public ICollection<ImportBatchError> Errors { get; set; } = new List<ImportBatchError>();
}
