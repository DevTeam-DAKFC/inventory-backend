using Inventory.Api.Models;

namespace Inventory.Api.InventoryMovements.Dtos;

public class InventoryMovementResponse
{
    public required string Id { get; init; }
    public required string ProductId { get; init; }
    public required string BranchId { get; init; }
    public required string UserId { get; init; }
    public required MovementType Type { get; init; }
    public required int Quantity { get; init; }
    public required int PreviousStock { get; init; }
    public required int ResultingStock { get; init; }
    public required string Reason { get; init; }
    public string? Notes { get; init; }
    public required DateTime CreatedAt { get; init; }

    public static InventoryMovementResponse FromEntity(InventoryMovement movement) => new()
    {
        Id = movement.Id.ToString(),
        ProductId = movement.ProductId.ToString(),
        BranchId = movement.BranchId.ToString(),
        UserId = movement.UserId.ToString(),
        Type = movement.Type,
        Quantity = movement.Quantity,
        PreviousStock = movement.PreviousStock,
        ResultingStock = movement.ResultingStock,
        Reason = movement.Reason,
        Notes = movement.Notes,
        CreatedAt = movement.CreatedAt
    };
}
