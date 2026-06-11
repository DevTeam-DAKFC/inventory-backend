namespace Inventory.Api.Notifications;

public interface IFirebaseMessagingClient
{
    Task SendMulticastAsync(
        IReadOnlyList<string> tokens,
        FcmNotification notification,
        CancellationToken cancellationToken);
}
