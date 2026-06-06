using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Auth;

public interface IAuthRegistrationService
{
    Task<RegisterResult> RegisterAsync(string name, string email, string password, CancellationToken cancellationToken);
}

public abstract record RegisterResult
{
    public sealed record Success(AppUser User, AccessToken Token) : RegisterResult;

    public sealed record EmailAlreadyRegistered : RegisterResult;
}

public class AuthRegistrationService : IAuthRegistrationService
{
    private readonly InventoryDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public AuthRegistrationService(
        InventoryDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterResult> RegisterAsync(
        string name,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);

        var emailTaken = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailTaken)
        {
            return new RegisterResult.EmailAlreadyRegistered();
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(password),
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        var token = _tokenService.CreateAccessToken(user);
        return new RegisterResult.Success(user, token);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
