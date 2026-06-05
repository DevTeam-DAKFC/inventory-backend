using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests.Auth;

public class AuthLoginEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public AuthLoginEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string email, string password, AppUser user)> SeedActiveUserAsync(
        string? name = null,
        string? password = null,
        UserRole role = UserRole.Collaborator)
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        var rawPassword = password ?? "Password123!";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = name ?? "Ana Gómez",
            Email = email,
            PasswordHash = hasher.Hash(rawPassword),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (email, rawPassword, user);
    }

    private async Task<(string email, string password)> SeedInactiveUserAsync()
    {
        var email = $"inactive-{Guid.NewGuid():N}@example.com";
        const string rawPassword = "Password123!";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        db.Users.Add(new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Inactive User",
            Email = email,
            PasswordHash = hasher.Hash(rawPassword),
            Role = UserRole.Collaborator,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (email, rawPassword);
    }

    [Fact]
    public async Task Returns_200_With_AuthLoginResponse_For_Valid_Credentials()
    {
        var (email, password, seeded) = await SeedActiveUserAsync(name: "Ana Gómez");

        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.True(body.GetProperty("expiresIn").GetInt32() > 0);

        var user = body.GetProperty("user");
        Assert.Equal(seeded.Id.ToString(), user.GetProperty("id").GetString());
        Assert.Equal("Ana Gómez", user.GetProperty("name").GetString());
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("collaborator", user.GetProperty("role").GetString());
        Assert.Equal(0, user.GetProperty("branchIds").GetArrayLength());
        Assert.True(user.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Response_Does_Not_Expose_PasswordHash()
    {
        var (email, password, _) = await SeedActiveUserAsync();

        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Accepts_Email_Case_Insensitively()
    {
        var (email, password, _) = await SeedActiveUserAsync();

        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email = email.ToUpperInvariant(), password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_Email_Returns_401_With_Generic_Message()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = $"missing-{Guid.NewGuid():N}@example.com",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Invalid email or password.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Wrong_Password_Returns_401_With_Same_Message_As_Unknown_Email()
    {
        var (email, _, _) = await SeedActiveUserAsync();

        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email, password = "the-wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Invalid email or password.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Inactive_User_Returns_401()
    {
        var (email, password) = await SeedInactiveUserAsync();

        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("unauthorized", error.GetProperty("code").GetString());
        Assert.Equal("Invalid email or password.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Missing_Email_Returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { password = "Password123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", body.GetProperty("error").GetProperty("code").GetString());
        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("email", fields);
    }

    [Fact]
    public async Task Invalid_Email_Format_Returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email = "not-an-email", password = "Password123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("email", fields);
    }

    [Fact]
    public async Task Missing_Password_Returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email = "someone@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("password", fields);
    }
}
