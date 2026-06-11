using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Notifications;

public sealed class FirebaseMessagingClient : IFirebaseMessagingClient
{
    private readonly Lazy<FirebaseMessaging> _messaging;

    public FirebaseMessagingClient(IOptions<FcmOptions> options)
    {
        var configured = options.Value;
        _messaging = new Lazy<FirebaseMessaging>(() => CreateMessaging(configured));
    }

    public async Task SendMulticastAsync(
        IReadOnlyList<string> tokens,
        FcmNotification notification,
        CancellationToken cancellationToken)
    {
        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = notification.Title,
                Body = notification.Body
            },
            Data = notification.Data.ToDictionary(entry => entry.Key, entry => entry.Value)
        };

        await _messaging.Value.SendEachForMulticastAsync(message, cancellationToken);
    }

    private static FirebaseMessaging CreateMessaging(FcmOptions options)
    {
        var appOptions = new AppOptions
        {
            Credential = GoogleCredential.FromFile(options.CredentialsPath!)
        };

        if (!string.IsNullOrWhiteSpace(options.ProjectId))
        {
            appOptions.ProjectId = options.ProjectId;
        }

        var app = FirebaseApp.Create(appOptions, $"inventory-api-{Guid.NewGuid():N}");
        return FirebaseMessaging.GetMessaging(app);
    }
}
