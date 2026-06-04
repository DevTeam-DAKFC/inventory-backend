using System.Net;

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
}
