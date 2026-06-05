using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Auth;

public interface IAuthCurrentUserService
{
    Task<CurrentUserResult> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public abstract record CurrentUserResult
{
    public sealed record Found(AppUser User) : CurrentUserResult;

    public sealed record Unauthorized : CurrentUserResult;
}

public class AuthCurrentUserService : IAuthCurrentUserService
{
    private readonly InventoryDbContext _db;

    public AuthCurrentUserService(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task<CurrentUserResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return new CurrentUserResult.Unauthorized();
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new CurrentUserResult.Unauthorized();
        }

        return new CurrentUserResult.Found(user);
    }
}
