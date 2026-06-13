namespace Inventory.Api.Notifications;

public interface IFcmNotificationSender
{
    Task SendAsync(FcmNotification notification, CancellationToken cancellationToken);
}
