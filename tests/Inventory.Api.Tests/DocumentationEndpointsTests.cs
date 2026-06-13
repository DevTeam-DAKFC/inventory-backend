using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.Api.Tests;

public class DocumentationEndpointsTests : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client;

    public DocumentationEndpointsTests(InventoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_OpenApi_Document_Returns_200_In_Development()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_OpenApi_Document_Exposes_Implemented_Endpoints_And_Schemas()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");

        AssertPathMethod(paths, "/health", "get");
        AssertPathMethod(paths, "/stock", "get");
        AssertPathMethod(paths, "/stock/lookup", "get");
        AssertPathMethod(paths, "/stock/{stockId}", "get");

        var schemas = document
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("ErrorResponse", out _));
        Assert.True(schemas.TryGetProperty("StockResponse", out _));
        Assert.True(schemas.TryGetProperty("StockProductResponse", out _));
        Assert.True(schemas.TryGetProperty("StockBranchResponse", out _));
    }

    [Fact]
    public async Task Get_Swagger_Document_Returns_200_In_Development()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected 2xx status code, got {(int)response.StatusCode} {response.StatusCode}: {content}");
    }

    [Fact]
    public async Task Get_Swagger_Ui_Returns_200_In_Development()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Scalar_Reference_Returns_200_In_Development()
    {
        var response = await _client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertPathMethod(JsonElement paths, string path, string method)
    {
        Assert.True(paths.TryGetProperty(path, out var pathItem), $"Expected OpenAPI path '{path}'.");
        Assert.True(pathItem.TryGetProperty(method, out _), $"Expected OpenAPI method '{method}' for '{path}'.");
    }

    [Fact]
    public async Task OpenApi_Documents_NotificationToken_Endpoints_And_Responses()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");

        var postResponses = paths
            .GetProperty("/notification-tokens")
            .GetProperty("post")
            .GetProperty("responses");
        Assert.True(postResponses.TryGetProperty("200", out _));
        Assert.True(postResponses.TryGetProperty("201", out _));
        Assert.True(postResponses.TryGetProperty("400", out _));
        Assert.True(postResponses.TryGetProperty("401", out _));
        Assert.True(postResponses.TryGetProperty("409", out _));

        var deleteResponses = paths
            .GetProperty("/notification-tokens/{tokenId}")
            .GetProperty("delete")
            .GetProperty("responses");
        Assert.True(deleteResponses.TryGetProperty("204", out _));
        Assert.True(deleteResponses.TryGetProperty("400", out _));
        Assert.True(deleteResponses.TryGetProperty("401", out _));
        Assert.True(deleteResponses.TryGetProperty("404", out _));
    }
}
