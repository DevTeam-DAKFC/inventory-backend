namespace Inventory.Api.Notifications;

public sealed class StockAlertNotificationService : IStockAlertNotificationService
{
    private readonly IFcmNotificationSender _sender;
    private readonly ILogger<StockAlertNotificationService> _logger;

    public StockAlertNotificationService(
        IFcmNotificationSender sender,
        ILogger<StockAlertNotificationService> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task NotifyIfSeverityIncreasedAsync(
        int previousQuantity,
        int resultingQuantity,
        int minStock,
        Guid productId,
        string productName,
        Guid branchId,
        string branchName,
        CancellationToken cancellationToken)
    {
        var previousSeverity = GetSeverity(previousQuantity, minStock);
        var resultingSeverity = GetSeverity(resultingQuantity, minStock);

        if (resultingSeverity <= previousSeverity)
        {
            return;
        }

        var notification = CreateNotification(
            resultingSeverity,
            productId,
            productName,
            branchId,
            branchName);

        try
        {
            await _sender.SendAsync(notification, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Failed to send stock alert for product {ProductId} at branch {BranchId}. Alert type: {AlertType}. Exception type: {ExceptionType}.",
                productId,
                branchId,
                notification.Data["type"],
                exception.GetType().Name);
        }
    }

    private static StockAlertSeverity GetSeverity(int quantity, int minStock) =>
        quantity switch
        {
            0 => StockAlertSeverity.OutOfStock,
            _ when quantity <= minStock => StockAlertSeverity.LowStock,
            _ => StockAlertSeverity.Normal
        };

    private static FcmNotification CreateNotification(
        StockAlertSeverity severity,
        Guid productId,
        string productName,
        Guid branchId,
        string branchName)
    {
        var (type, title, body) = severity switch
        {
            StockAlertSeverity.LowStock => (
                "low_stock",
                "Stock bajo detectado",
                $"{productName} tiene stock bajo en {branchName}."),
            StockAlertSeverity.OutOfStock => (
                "out_of_stock",
                "Producto sin existencias",
                $"{productName} se quedó sin existencias en {branchName}."),
            _ => throw new InvalidOperationException("Normal stock does not produce an alert.")
        };

        return new FcmNotification(
            title,
            body,
            new Dictionary<string, string>
            {
                ["type"] = type,
                ["productId"] = productId.ToString(),
                ["branchId"] = branchId.ToString()
            });
    }

    private enum StockAlertSeverity
    {
        Normal,
        LowStock,
        OutOfStock
    }
}
