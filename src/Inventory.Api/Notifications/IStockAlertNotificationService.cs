namespace Inventory.Api.Notifications;

public interface IStockAlertNotificationService
{
    Task NotifyIfSeverityIncreasedAsync(
        int previousQuantity,
        int resultingQuantity,
        int minStock,
        Guid productId,
        string productName,
        Guid branchId,
        string branchName,
        CancellationToken cancellationToken);
}
