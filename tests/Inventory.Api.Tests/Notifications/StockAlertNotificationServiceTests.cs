using Inventory.Api.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inventory.Api.Tests.Notifications;

public class StockAlertNotificationServiceTests
{
    [Fact]
    public async Task Normal_To_LowStock_Sends_LowStock_Notification()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var productId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await service.NotifyIfSeverityIncreasedAsync(
            previousQuantity: 6,
            resultingQuantity: 5,
            minStock: 5,
            productId,
            "Rice",
            branchId,
            "Central",
            CancellationToken.None);

        var notification = Assert.Single(sender.Notifications);
        Assert.Equal("Stock bajo detectado", notification.Title);
        Assert.Equal("low_stock", notification.Data["type"]);
        Assert.Equal(productId.ToString(), notification.Data["productId"]);
        Assert.Equal(branchId.ToString(), notification.Data["branchId"]);
    }

    [Theory]
    [InlineData(6, 0, "out_of_stock")]
    [InlineData(5, 0, "out_of_stock")]
    public async Task Transition_To_OutOfStock_Sends_OutOfStock_Notification(
        int previousQuantity,
        int resultingQuantity,
        string expectedType)
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);

        await service.NotifyIfSeverityIncreasedAsync(
            previousQuantity,
            resultingQuantity,
            minStock: 5,
            Guid.NewGuid(),
            "Rice",
            Guid.NewGuid(),
            "Central",
            CancellationToken.None);

        var notification = Assert.Single(sender.Notifications);
        Assert.Equal("Producto sin existencias", notification.Title);
        Assert.Equal(expectedType, notification.Data["type"]);
    }

    [Theory]
    [InlineData(8, 7)]
    [InlineData(5, 4)]
    [InlineData(0, 0)]
    [InlineData(4, 8)]
    [InlineData(0, 5)]
    public async Task Same_Or_Lower_Severity_Does_Not_Send(
        int previousQuantity,
        int resultingQuantity)
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);

        await service.NotifyIfSeverityIncreasedAsync(
            previousQuantity,
            resultingQuantity,
            minStock: 5,
            Guid.NewGuid(),
            "Rice",
            Guid.NewGuid(),
            "Central",
            CancellationToken.None);

        Assert.Empty(sender.Notifications);
    }

    [Fact]
    public async Task Sender_Failure_Is_Handled()
    {
        var service = CreateService(new ThrowingSender());

        var exception = await Record.ExceptionAsync(() =>
            service.NotifyIfSeverityIncreasedAsync(
                previousQuantity: 6,
                resultingQuantity: 5,
                minStock: 5,
                Guid.NewGuid(),
                "Rice",
                Guid.NewGuid(),
                "Central",
                CancellationToken.None));

        Assert.Null(exception);
    }

    private static StockAlertNotificationService CreateService(IFcmNotificationSender sender) =>
        new(sender, NullLogger<StockAlertNotificationService>.Instance);

    private sealed class RecordingSender : IFcmNotificationSender
    {
        public List<FcmNotification> Notifications { get; } = [];

        public Task SendAsync(FcmNotification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSender : IFcmNotificationSender
    {
        public Task SendAsync(FcmNotification notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Firebase unavailable.");
    }
}
