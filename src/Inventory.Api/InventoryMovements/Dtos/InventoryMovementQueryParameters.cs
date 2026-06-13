using Inventory.Api.Models;

namespace Inventory.Api.InventoryMovements.Dtos;

public class InventoryMovementQueryParameters
{
    public string? BranchId { get; init; }
    public string? ProductId { get; init; }
    public MovementType? Type { get; init; }
    public string? UserId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
