using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Notifications;

public sealed class FirebaseFcmNotificationSender : IFcmNotificationSender
{
    private const int MaxTokensPerRequest = 500;

    private readonly InventoryDbContext _db;
    private readonly IFirebaseMessagingClient _messagingClient;

    public FirebaseFcmNotificationSender(
        InventoryDbContext db,
        IFirebaseMessagingClient messagingClient)
    {
        _db = db;
        _messagingClient = messagingClient;
    }

    public async Task SendAsync(FcmNotification notification, CancellationToken cancellationToken)
    {
        var tokens = await _db.NotificationTokens
            .AsNoTracking()
            .Select(notificationToken => notificationToken.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return;
        }

        foreach (var batch in tokens.Chunk(MaxTokensPerRequest))
        {
            await _messagingClient.SendMulticastAsync(batch, notification, cancellationToken);
        }
    }
}
