using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Contracts.Products;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Inventory.Api.ProductLookup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class ProductLookupEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ProductLookupEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Lookup_Product_Returns_External_Product_Suggestion()
    {
        await _factory.ResetDatabaseAsync();
        using var client = CreateClient(new ExternalProductLookupResult.Success(
            new ExternalProductSuggestion(
                "3017624010701",
                "Nutella",
                "Ferrero",
                "Spreads",
                "https://images.example.test/nutella.jpg",
                "open_food_facts")));

        var response = await client.GetAsync("/product-lookup/3017624010701");
        var suggestion = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("3017624010701", suggestion.GetProperty("barcode").GetString());
        Assert.Equal("Nutella", suggestion.GetProperty("name").GetString());
        Assert.Equal("Ferrero", suggestion.GetProperty("brand").GetString());
        Assert.Equal("Spreads", suggestion.GetProperty("category").GetString());
        Assert.Equal("https://images.example.test/nutella.jpg", suggestion.GetProperty("imageUrl").GetString());
        Assert.Equal("open_food_facts", suggestion.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("%20")]
    [InlineData("%20%20%20")]
    public async Task Lookup_Product_Returns_400_For_Empty_Or_Whitespace_Barcode(string barcode)
    {
        using var client = CreateClient(new ExternalProductLookupResult.ProviderUnavailable());

        var response = await client.GetAsync($"/product-lookup/{barcode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorResponseAsync(response, "validation_error", "barcode");
    }

    [Fact]
    public async Task Lookup_Product_Returns_404_When_Provider_Does_Not_Find_Product()
    {
        using var client = CreateClient(new ExternalProductLookupResult.NotFound());

        var response = await client.GetAsync("/product-lookup/0000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorResponseAsync(response, "product_not_found", "barcode");
    }

    [Fact]
    public async Task Lookup_Product_Returns_503_When_Provider_Fails()
    {
        using var client = CreateClient(new ExternalProductLookupResult.ProviderUnavailable());

        var response = await client.GetAsync("/product-lookup/3017624010701");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertErrorResponseAsync(response, "service_unavailable");
    }

    [Fact]
    public async Task Lookup_Product_Does_Not_Persist_Product()
    {
        await _factory.ResetDatabaseAsync();
        using var client = CreateClient(new ExternalProductLookupResult.Success(
            new ExternalProductSuggestion(
                "3017624010701",
                "Nutella",
                "Ferrero",
                "Spreads",
                "https://images.example.test/nutella.jpg",
                "open_food_facts")));

        var response = await client.GetAsync("/product-lookup/3017624010701");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(0, await dbContext.Products.CountAsync());
    }

    [Fact]
    public async Task Lookup_Product_Returns_401_Without_Authentication()
    {
        using var client = CreateClient(
            new ExternalProductLookupResult.ProviderUnavailable(),
            authenticated: false);

        var response = await client.GetAsync("/product-lookup/3017624010701");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorResponseAsync(response, "unauthorized");
    }

    private HttpClient CreateClient(
        ExternalProductLookupResult result,
        bool authenticated = true)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IExternalProductLookupService>(
                    new StubExternalProductLookupService(result));
            });
        });

        var client = factory.CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add("X-Test-User-Role", UserRole.Collaborator.ToString());
        }

        return client;
    }

    private static async Task AssertErrorResponseAsync(
        HttpResponseMessage response,
        string expectedCode,
        string? expectedField = null)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = payload.GetProperty("error");

        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));

        if (expectedField is null)
        {
            return;
        }

        var details = error.GetProperty("details").EnumerateArray().ToArray();
        Assert.Contains(details, detail => detail.GetProperty("field").GetString() == expectedField);
    }

    private sealed class StubExternalProductLookupService : IExternalProductLookupService
    {
        private readonly ExternalProductLookupResult _result;

        public StubExternalProductLookupService(ExternalProductLookupResult result)
        {
            _result = result;
        }

        public Task<ExternalProductLookupResult> LookupAsync(
            string barcode,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
