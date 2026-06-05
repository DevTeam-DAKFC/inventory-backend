using Inventory.Api.Models;

namespace Inventory.Api.Auth.Dtos;

public class UserDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required UserRole Role { get; init; }
    public IReadOnlyList<string> BranchIds { get; init; } = Array.Empty<string>();
    public required bool IsActive { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    public static UserDto FromAppUser(AppUser user) => new()
    {
        Id = user.Id.ToString(),
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        BranchIds = Array.Empty<string>(),
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };
}
