using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Auth;

public interface IAuthLoginService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

public abstract record LoginResult
{
    public sealed record Success(AppUser User, AccessToken Token) : LoginResult;

    public sealed record InvalidCredentials : LoginResult;
}

public class AuthLoginService : IAuthLoginService
{
    private readonly InventoryDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthLoginService(
        InventoryDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new LoginResult.InvalidCredentials();
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            return new LoginResult.InvalidCredentials();
        }

        var token = _tokenService.CreateAccessToken(user);
        return new LoginResult.Success(user, token);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
