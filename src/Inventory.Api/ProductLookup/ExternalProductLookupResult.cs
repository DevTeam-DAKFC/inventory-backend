using Inventory.Api.Contracts.Products;

namespace Inventory.Api.ProductLookup;

public abstract record ExternalProductLookupResult
{
    public sealed record Success(ExternalProductSuggestion Suggestion) : ExternalProductLookupResult;
    public sealed record NotFound : ExternalProductLookupResult;
    public sealed record ProviderUnavailable : ExternalProductLookupResult;
}
