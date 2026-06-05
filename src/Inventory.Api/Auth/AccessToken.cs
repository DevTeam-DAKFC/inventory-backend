namespace Inventory.Api.Auth;

public record AccessToken(string Value, string TokenType, int ExpiresInSeconds, DateTime ExpiresAtUtc);
