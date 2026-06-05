using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Inventory.Api.Auth;
using Inventory.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private const string SecretKey = "unit-test-secret-key-at-least-32-bytes-long-xxxxxxxxxxxxxxxxxxxxxx";
    private const string Issuer = "inventory-api-unit-tests";
    private const string Audience = "inventory-mobile-unit-tests";

    private static AppUser SampleUser() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Ana Gómez",
        Email = "ana@example.com",
        PasswordHash = "irrelevant-for-token-generation",
        Role = UserRole.Collaborator,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static JwtTokenService BuildService(int expiresInMinutes = 60, TimeProvider? timeProvider = null)
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = SecretKey,
            Issuer = Issuer,
            Audience = Audience,
            ExpiresInMinutes = expiresInMinutes
        });

        return new JwtTokenService(options, timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public void CreateAccessToken_Generates_NonEmpty_Token_With_Bearer_Type()
    {
        var service = BuildService();

        var accessToken = service.CreateAccessToken(SampleUser());

        Assert.False(string.IsNullOrWhiteSpace(accessToken.Value));
        Assert.Equal("Bearer", accessToken.TokenType);
    }

    [Fact]
    public void CreateAccessToken_Includes_Expiration_Metadata()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var service = BuildService(expiresInMinutes: 30, timeProvider: timeProvider);

        var accessToken = service.CreateAccessToken(SampleUser());

        Assert.Equal(30 * 60, accessToken.ExpiresInSeconds);
        Assert.Equal(fixedNow.UtcDateTime.AddMinutes(30), accessToken.ExpiresAtUtc);
    }

    [Fact]
    public void CreateAccessToken_Produces_Token_That_Validates_With_Configured_Issuer_Audience_Key()
    {
        var service = BuildService();
        var user = SampleUser();

        var accessToken = service.CreateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = handler.ValidateToken(accessToken.Value, validationParameters, out var validatedToken);

        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(user.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(user.Role.ToString(), principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public void CreateAccessToken_Rejects_Validation_With_Wrong_Key()
    {
        var service = BuildService();
        var accessToken = service.CreateAccessToken(SampleUser());

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("a-different-key-also-32-bytes-or-more-zzzzzzzzzzzzzzzzzzzzzzzzz")),
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => handler.ValidateToken(accessToken.Value, validationParameters, out _));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
