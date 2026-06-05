using System.Security.Claims;
using System.Text.Encodings.Web;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Tests;

public class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    private const string UserIdHeaderName = "X-Test-User-Id";
    private const string UserRoleHeaderName = "X-Test-User-Role";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserRoleHeaderName, out var roleHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Enum.TryParse<UserRole>(roleHeader.ToString(), ignoreCase: true, out var role))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid test user role."));
        }

        var userId = Request.Headers.TryGetValue(UserIdHeaderName, out var userIdHeader)
            ? userIdHeader.ToString()
            : Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
