namespace Inventory.Api.Notifications;

public sealed class NoOpFcmNotificationSender : IFcmNotificationSender
{
    public Task SendAsync(FcmNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
