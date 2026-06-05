using Inventory.Api.Models;

namespace Inventory.Api.Auth;

public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user);
}
