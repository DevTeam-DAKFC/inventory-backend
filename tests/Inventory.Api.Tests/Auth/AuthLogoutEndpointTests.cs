using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests.Auth;

public class AuthLogoutEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public AuthLogoutEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> SeedActiveUserAndIssueTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Logout User",
            Email = $"logout-{Guid.NewGuid():N}@example.com",
            PasswordHash = "n/a",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return tokens.CreateAccessToken(user).Value;
    }

    private static HttpRequestMessage Post(string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return request;
    }

    [Fact]
    public async Task Returns_204_With_Valid_Token()
    {
        var token = await SeedActiveUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task Returns_401_Without_Token_With_ErrorResponse_Envelope()
    {
        var response = await _client.SendAsync(Post(bearerToken: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Authentication required.", error.GetProperty("message").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));
    }

    [Fact]
    public async Task Returns_401_With_Invalid_Token_With_ErrorResponse_Envelope()
    {
        var response = await _client.SendAsync(Post(bearerToken: "not.a.real.token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Authentication required.", error.GetProperty("message").GetString());
    }
}
