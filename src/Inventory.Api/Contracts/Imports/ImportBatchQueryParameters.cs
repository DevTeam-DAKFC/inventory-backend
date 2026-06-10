namespace Inventory.Api.Contracts.Imports;

public class ImportBatchQueryParameters
{
    public string? Status { get; init; }
    public string? ImportedBy { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
