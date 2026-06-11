namespace Inventory.Api.Contracts.NotificationTokens;

public sealed record NotificationTokenResponse(
    Guid Id,
    string Token,
    string Platform,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
