using System.Text.Json.Serialization;

namespace Inventory.Api.ProductLookup.OpenFoodFacts;

internal sealed class OpenFoodFactsProductResponse
{
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("product")]
    public OpenFoodFactsProduct? Product { get; set; }
}

internal sealed class OpenFoodFactsProduct
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("brands")]
    public string? Brands { get; set; }

    [JsonPropertyName("categories")]
    public string? Categories { get; set; }

    [JsonPropertyName("image_front_url")]
    public string? ImageFrontUrl { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }
}
