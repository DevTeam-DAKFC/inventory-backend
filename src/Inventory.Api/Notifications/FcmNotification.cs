namespace Inventory.Api.Notifications;

public sealed record FcmNotification(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data);
