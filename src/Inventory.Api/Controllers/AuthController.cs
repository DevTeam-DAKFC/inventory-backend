using Inventory.Api.Auth;
using Inventory.Api.Auth.Dtos;
using Inventory.Api.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthRegistrationService _registrationService;

    public AuthController(IAuthRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthLoginResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] AuthRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _registrationService.RegisterAsync(
            request.Name,
            request.Email,
            request.Password,
            cancellationToken);

        return result switch
        {
            RegisterResult.Success success => Created(
                (string?)null,
                AuthLoginResponse.From(success.Token, UserDto.FromAppUser(success.User))),

            RegisterResult.EmailAlreadyRegistered => Conflict(new ErrorResponse
            {
                Error = new ErrorBody
                {
                    Code = "conflict",
                    Message = "Email already registered.",
                    Details = [new FieldError("email", "An account with this email already exists.")],
                    RequestId = HttpContext.TraceIdentifier
                }
            }),

            _ => throw new InvalidOperationException("Unhandled registration result.")
        };
    }
}
