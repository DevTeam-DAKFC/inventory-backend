namespace Inventory.Api.Models;

public class Stock
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid BranchId { get; set; }
    public int AvailableQuantity { get; set; }
    public int MinStock { get; set; }
    public Guid? LastMovementId { get; set; }
    public DateTime? LastMovementAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Product Product { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public InventoryMovement? LastMovement { get; set; }
}
