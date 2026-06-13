using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Auth;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Tests;

public class NotificationTokenEndpointTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;
    private readonly HttpClient _client;

    public NotificationTokenEndpointTests(InventoryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_NotificationTokens_Returns_201_And_Persists_Token_For_Current_User()
    {
        await _factory.ResetDatabaseAsync();
        var (user, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = "  fcm-token-new  ", platform = "android" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fcm-token-new", body.GetProperty("token").GetString());
        Assert.Equal("android", body.GetProperty("platform").GetString());
        Assert.True(body.TryGetProperty("createdAt", out _));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stored = await db.NotificationTokens.SingleAsync();
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal("fcm-token-new", stored.Token);
        Assert.Equal(PlatformType.Android, stored.Platform);
    }

    [Fact]
    public async Task Post_NotificationTokens_Returns_200_And_Updates_Existing_Token_Without_Duplicate()
    {
        await _factory.ResetDatabaseAsync();
        var firstUser = await SeedUserAsync();
        var (currentUser, accessToken) = await SeedUserAndIssueTokenAsync();
        var existingToken = await SeedNotificationTokenAsync(firstUser.Id, "shared-fcm-token", PlatformType.Android);

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = "shared-fcm-token", platform = "web" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(existingToken.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("web", body.GetProperty("platform").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var stored = await db.NotificationTokens.SingleAsync();
        Assert.Equal(currentUser.Id, stored.UserId);
        Assert.Equal(PlatformType.Web, stored.Platform);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Post_NotificationTokens_Rejects_Empty_Token()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = "   ", platform = "android" }));

        await AssertErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_error", "token");
    }

    [Fact]
    public async Task Post_NotificationTokens_Rejects_Token_Over_500_Characters()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = new string('x', 501), platform = "android" }));

        await AssertErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_error", "token");
    }

    [Fact]
    public async Task Post_NotificationTokens_Rejects_Invalid_Platform()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = "fcm-token", platform = "windows" }));

        await AssertErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_error", "platform");
    }

    [Fact]
    public async Task Post_NotificationTokens_Returns_401_Without_Jwt()
    {
        await _factory.ResetDatabaseAsync();

        var response = await _client.PostAsJsonAsync(
            "/notification-tokens",
            new { token = "fcm-token", platform = "ios" });

        await AssertErrorResponseAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Post_NotificationTokens_Response_Does_Not_Expose_UserId()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Post(
            accessToken,
            new { token = "private-owner-token", platform = "ios" }));

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user_id", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delete_NotificationToken_Removes_Own_Token_And_Returns_204()
    {
        await _factory.ResetDatabaseAsync();
        var (user, accessToken) = await SeedUserAndIssueTokenAsync();
        var notificationToken = await SeedNotificationTokenAsync(user.Id, "own-token", PlatformType.Android);

        var response = await _client.SendAsync(Delete(accessToken, notificationToken.Id.ToString()));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await db.NotificationTokens.ToListAsync());
    }

    [Fact]
    public async Task Delete_NotificationToken_Returns_404_When_Token_Does_Not_Exist()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Delete(accessToken, Guid.NewGuid().ToString()));

        await AssertErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found", "tokenId");
    }

    [Fact]
    public async Task Delete_NotificationToken_Returns_404_For_Another_Users_Token()
    {
        await _factory.ResetDatabaseAsync();
        var owner = await SeedUserAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();
        var notificationToken = await SeedNotificationTokenAsync(owner.Id, "other-user-token", PlatformType.Ios);

        var response = await _client.SendAsync(Delete(accessToken, notificationToken.Id.ToString()));

        await AssertErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found", "tokenId");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.True(await db.NotificationTokens.AnyAsync(token => token.Id == notificationToken.Id));
    }

    [Fact]
    public async Task Delete_NotificationToken_Returns_400_When_TokenId_Is_Invalid()
    {
        await _factory.ResetDatabaseAsync();
        var (_, accessToken) = await SeedUserAndIssueTokenAsync();

        var response = await _client.SendAsync(Delete(accessToken, "not-a-guid"));

        await AssertErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_error", "tokenId");
    }

    private async Task<(AppUser user, string accessToken)> SeedUserAndIssueTokenAsync()
    {
        var user = await SeedUserAsync();

        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return (user, tokens.CreateAccessToken(user).Value);
    }

    private async Task<AppUser> SeedUserAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Notification User",
            Email = $"notifications-{Guid.NewGuid():N}@example.com",
            PasswordHash = "unused",
            Role = UserRole.Collaborator,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<NotificationToken> SeedNotificationTokenAsync(
        Guid userId,
        string token,
        PlatformType platform)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var notificationToken = new NotificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = platform,
            CreatedAt = DateTime.UtcNow
        };

        db.NotificationTokens.Add(notificationToken);
        await db.SaveChangesAsync();
        return notificationToken;
    }

    private static HttpRequestMessage Post(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/notification-tokens")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage Delete(string accessToken, string tokenId)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/notification-tokens/{tokenId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task AssertErrorResponseAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string? expectedField = null)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("requestId").GetString()));

        if (expectedField is not null)
        {
            Assert.Contains(
                error.GetProperty("details").EnumerateArray(),
                detail => detail.GetProperty("field").GetString() == expectedField);
        }
    }
}
