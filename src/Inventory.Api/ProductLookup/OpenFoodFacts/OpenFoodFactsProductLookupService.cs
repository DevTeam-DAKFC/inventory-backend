using System.Net;
using System.Net.Http.Json;
using Inventory.Api.Contracts.Products;

namespace Inventory.Api.ProductLookup.OpenFoodFacts;

public class OpenFoodFactsProductLookupService : IExternalProductLookupService
{
    private const string SourceName = "open_food_facts";

    private readonly HttpClient _httpClient;

    public OpenFoodFactsProductLookupService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ExternalProductLookupResult> LookupAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(
                $"/api/v2/product/{Uri.EscapeDataString(barcode)}.json",
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ExternalProductLookupResult.ProviderUnavailable();
        }
        catch (HttpRequestException)
        {
            return new ExternalProductLookupResult.ProviderUnavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ExternalProductLookupResult.NotFound();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }

            OpenFoodFactsProductResponse? providerResponse;
            try
            {
                providerResponse = await response.Content.ReadFromJsonAsync<OpenFoodFactsProductResponse>(
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }
            catch (HttpRequestException)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }
            catch (System.Text.Json.JsonException)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }

            if (providerResponse is null)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }

            if (providerResponse.Status == 0)
            {
                return new ExternalProductLookupResult.NotFound();
            }

            if (providerResponse.Status != 1 || providerResponse.Product is null)
            {
                return new ExternalProductLookupResult.ProviderUnavailable();
            }

            var imageUrl = TrimToNull(providerResponse.Product.ImageFrontUrl)
                ?? TrimToNull(providerResponse.Product.ImageUrl);

            if (imageUrl is not null &&
                !Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                imageUrl = null;
            }

            return new ExternalProductLookupResult.Success(new ExternalProductSuggestion(
                barcode,
                TrimToNull(providerResponse.Product.ProductName),
                TrimToNull(providerResponse.Product.Brands),
                TrimToNull(providerResponse.Product.Categories),
                imageUrl,
                SourceName));
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
