using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests.Auth;

public class AuthRegisterEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public AuthRegisterEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object ValidPayload(string? email = null) => new
    {
        name = "Ana Gómez",
        email = email ?? $"ana-{Guid.NewGuid():N}@example.com",
        password = "Password123!"
    };

    [Fact]
    public async Task Returns_201_With_AuthLoginResponse_For_Valid_Input()
    {
        var payload = ValidPayload();

        var response = await _client.PostAsJsonAsync("/auth/register", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.True(body.GetProperty("expiresIn").GetInt32() > 0);

        var user = body.GetProperty("user");
        Assert.False(string.IsNullOrWhiteSpace(user.GetProperty("id").GetString()));
        Assert.Equal("Ana Gómez", user.GetProperty("name").GetString());
        Assert.Equal(((dynamic)payload).email, user.GetProperty("email").GetString());
        Assert.Equal("collaborator", user.GetProperty("role").GetString());
        Assert.Equal(0, user.GetProperty("branchIds").GetArrayLength());
        Assert.True(user.GetProperty("isActive").GetBoolean());
        Assert.True(user.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Response_Does_Not_Expose_PasswordHash()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", ValidPayload());
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"password\"", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_Stored_In_Db_Is_Hashed()
    {
        const string rawPassword = "Password123!";
        var email = $"hash-check-{Guid.NewGuid():N}@example.com";

        var response = await _client.PostAsJsonAsync(
            "/auth/register",
            new { name = "Bruno", email, password = rawPassword });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);

        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.NotEqual(rawPassword, user.PasswordHash);

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.True(hasher.Verify(rawPassword, user.PasswordHash));
    }

    [Fact]
    public async Task Duplicate_Email_Returns_409_With_Conflict_Code()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var first = await _client.PostAsJsonAsync(
            "/auth/register",
            new { name = "First", email, password = "Password123!" });
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync(
            "/auth/register",
            new { name = "Second", email, password = "Password123!" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("conflict", body.GetProperty("error").GetProperty("code").GetString());
        var details = body.GetProperty("error").GetProperty("details");
        Assert.Equal("email", details[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Duplicate_Email_Is_Case_Insensitive()
    {
        var email = $"case-{Guid.NewGuid():N}@example.com";
        var first = await _client.PostAsJsonAsync(
            "/auth/register",
            new { name = "First", email = email.ToLowerInvariant(), password = "Password123!" });
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync(
            "/auth/register",
            new { name = "Second", email = email.ToUpperInvariant(), password = "Password123!" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Missing_Name_Returns_400_With_Validation_Error()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            email = $"no-name-{Guid.NewGuid():N}@example.com",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", body.GetProperty("error").GetProperty("code").GetString());

        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("name", fields);
    }

    [Fact]
    public async Task Invalid_Email_Returns_400()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            name = "Carla",
            email = "not-an-email",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("email", fields);
    }

    [Fact]
    public async Task Short_Password_Returns_400()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            name = "Diego",
            email = $"short-pw-{Guid.NewGuid():N}@example.com",
            password = "abc12"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.GetProperty("error").GetProperty("details")
            .EnumerateArray()
            .Select(d => d.GetProperty("field").GetString())
            .ToList();
        Assert.Contains("password", fields);
    }
}
