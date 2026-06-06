namespace Inventory.Api.ProductLookup;

public class ProductLookupOptions
{
    public const string SectionName = "ProductLookup";

    public string Provider { get; set; } = "OpenFoodFacts";
    public string BaseUrl { get; set; } = "https://world.openfoodfacts.org";
    public string UserAgent { get; set; } = "InventoryBackend/1.0";
    public int TimeoutSeconds { get; set; } = 5;
}
