namespace Inventory.Api.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<InventoryMovement> InventoryMovements { get; set; } = new List<InventoryMovement>();
    public ICollection<NotificationToken> NotificationTokens { get; set; } = new List<NotificationToken>();
    public ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();
}
