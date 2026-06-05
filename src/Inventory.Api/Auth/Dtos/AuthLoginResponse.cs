namespace Inventory.Api.Auth.Dtos;

public class AuthLoginResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required int ExpiresIn { get; init; }
    public required UserDto User { get; init; }

    public static AuthLoginResponse From(AccessToken token, UserDto user) => new()
    {
        AccessToken = token.Value,
        TokenType = token.TokenType,
        ExpiresIn = token.ExpiresInSeconds,
        User = user
    };
}
