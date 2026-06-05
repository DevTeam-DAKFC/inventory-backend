namespace Inventory.Api.Dtos;

public record BranchResponse(
    Guid Id,
    string Name,
    string? Address,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
