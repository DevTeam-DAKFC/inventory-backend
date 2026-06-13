using System.ComponentModel.DataAnnotations;
using Inventory.Api.Models;

namespace Inventory.Api.InventoryMovements.Dtos;

public class InventoryMovementCreateRequest
{
    [Required]
    public string? ProductId { get; init; }

    [Required]
    public string? BranchId { get; init; }

    [Required]
    public MovementType? Type { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string? Reason { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}
