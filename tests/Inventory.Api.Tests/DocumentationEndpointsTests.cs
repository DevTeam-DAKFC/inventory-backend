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
    public async Task Get_Scalar_Reference_Returns_200_In_Development()
    {
        var response = await _client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
