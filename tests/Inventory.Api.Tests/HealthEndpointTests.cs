using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.Api.Tests;

public class HealthEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(InventoryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Health_Returns_200_Ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_Returns_Expected_Payload()
    {
        var payload = await _client.GetFromJsonAsync<JsonElement>("/health");

        Assert.Equal("ok", payload.GetProperty("status").GetString());
        Assert.Equal("Inventory.Api", payload.GetProperty("service").GetString());
    }
}
