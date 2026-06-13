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
    private readonly IAuthLoginService _loginService;
    private readonly IAuthCurrentUserService _currentUserService;

    public AuthController(
        IAuthRegistrationService registrationService,
        IAuthLoginService loginService,
        IAuthCurrentUserService currentUserService)
    {
        _registrationService = registrationService;
        _loginService = loginService;
        _currentUserService = currentUserService;
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

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] AuthLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _loginService.LoginAsync(
            request.Email,
            request.Password,
            cancellationToken);

        return result switch
        {
            LoginResult.Success success => Ok(
                AuthLoginResponse.From(success.Token, UserDto.FromAppUser(success.User))),

            LoginResult.InvalidCredentials => Unauthorized(new ErrorResponse
            {
                Error = new ErrorBody
                {
                    Code = "unauthorized",
                    Message = "Invalid email or password.",
                    RequestId = HttpContext.TraceIdentifier
                }
            }),

            _ => throw new InvalidOperationException("Unhandled login result.")
        };
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);

        return result switch
        {
            CurrentUserResult.Found found => Ok(UserDto.FromAppUser(found.User)),

            CurrentUserResult.Unauthorized => Unauthorized(new ErrorResponse
            {
                Error = new ErrorBody
                {
                    Code = "unauthorized",
                    Message = "Authentication required.",
                    RequestId = HttpContext.TraceIdentifier
                }
            }),

            _ => throw new InvalidOperationException("Unhandled current-user result.")
        };
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        // MVP: stateless logout — the client discards the token; no server-side blacklist.
        return NoContent();
    }
}
