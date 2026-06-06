using Inventory.Api.Contracts.Common;
using Inventory.Api.Contracts.Errors;
using Inventory.Api.Contracts.Imports;

namespace Inventory.Api.Imports;

public interface IProductCsvImportService
{
    Task<ImportProductsResult> ImportProductsAsync(
        Guid importedBy,
        IFormFile file,
        CancellationToken cancellationToken);

    Task<ListImportBatchesResult> ListBatchesAsync(
        ImportBatchQueryParameters query,
        CancellationToken cancellationToken);

    Task<GetImportBatchResult> GetBatchAsync(
        string batchId,
        CancellationToken cancellationToken);

    Task<ListImportBatchErrorsResult> ListBatchErrorsAsync(
        string batchId,
        ImportBatchErrorQueryParameters query,
        CancellationToken cancellationToken);
}

public abstract record ImportProductsResult
{
    public sealed record Success(ImportBatchResponse Batch) : ImportProductsResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : ImportProductsResult;
}

public abstract record ListImportBatchesResult
{
    public sealed record Success(PaginatedResponse<ImportBatchResponse> Page) : ListImportBatchesResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : ListImportBatchesResult;
}

public abstract record GetImportBatchResult
{
    public sealed record Success(ImportBatchResponse Batch) : GetImportBatchResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : GetImportBatchResult;
    public sealed record NotFound : GetImportBatchResult;
}

public abstract record ListImportBatchErrorsResult
{
    public sealed record Success(PaginatedResponse<ImportBatchErrorResponse> Page) : ListImportBatchErrorsResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : ListImportBatchErrorsResult;
    public sealed record BatchNotFound : ListImportBatchErrorsResult;
}
