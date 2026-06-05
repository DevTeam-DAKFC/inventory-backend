using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests.Auth;

public class AuthMeEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public AuthMeEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(AppUser user, string token)> SeedActiveUserAndIssueTokenAsync(string? name = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = name ?? "Ana Gómez",
            Email = $"me-{Guid.NewGuid():N}@example.com",
            PasswordHash = "unused-for-me-endpoint",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = tokens.CreateAccessToken(user);
        return (user, token.Value);
    }

    private string IssueTokenForUnsavedUser(UserRole role = UserRole.Collaborator)
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var phantom = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Phantom",
            Email = $"phantom-{Guid.NewGuid():N}@example.com",
            PasswordHash = "n/a",
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        return tokens.CreateAccessToken(phantom).Value;
    }

    private async Task<string> SeedInactiveUserAndIssueTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Inactive",
            Email = $"inactive-me-{Guid.NewGuid():N}@example.com",
            PasswordHash = "n/a",
            Role = UserRole.Collaborator,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return tokens.CreateAccessToken(user).Value;
    }

    private HttpRequestMessage Get(string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        return request;
    }

    [Fact]
    public async Task Returns_200_With_UserDto_For_Valid_Token()
    {
        var (user, token) = await SeedActiveUserAndIssueTokenAsync(name: "Ana Gómez");

        var response = await _client.SendAsync(Get(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("Ana Gómez", body.GetProperty("name").GetString());
        Assert.Equal(user.Email, body.GetProperty("email").GetString());
        Assert.Equal("collaborator", body.GetProperty("role").GetString());
        Assert.Equal(0, body.GetProperty("branchIds").GetArrayLength());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.True(body.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Response_Does_Not_Expose_PasswordHash()
    {
        var (_, token) = await SeedActiveUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Get(token));
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_401_Without_Token_With_ErrorResponse_Envelope()
    {
        var response = await _client.SendAsync(Get(bearerToken: null));

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
        var response = await _client.SendAsync(Get(bearerToken: "not.a.real.token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Authentication required.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Returns_401_When_Token_User_Does_Not_Exist()
    {
        var token = IssueTokenForUnsavedUser();

        var response = await _client.SendAsync(Get(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Returns_401_When_User_Is_Inactive_With_ErrorResponse()
    {
        var token = await SeedInactiveUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Get(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unauthorized", body.GetProperty("error").GetProperty("code").GetString());
    }
}
