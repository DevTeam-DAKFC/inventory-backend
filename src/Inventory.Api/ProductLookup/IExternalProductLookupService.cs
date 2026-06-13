namespace Inventory.Api.ProductLookup;

public interface IExternalProductLookupService
{
    Task<ExternalProductLookupResult> LookupAsync(
        string barcode,
        CancellationToken cancellationToken);
}
