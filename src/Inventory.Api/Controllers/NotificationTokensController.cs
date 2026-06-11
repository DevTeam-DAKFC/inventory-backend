using Inventory.Api.Auth;
using Inventory.Api.Common.Errors;
using Inventory.Api.Contracts.NotificationTokens;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("notification-tokens")]
[Produces("application/json")]
public class NotificationTokensController : ControllerBase
{
    private const string UniqueTokenIndex = "UX_notification_tokens_token";

    private readonly InventoryDbContext _dbContext;
    private readonly IAuthCurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public NotificationTokensController(
        InventoryDbContext dbContext,
        IAuthCurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    [HttpPost]
    [ProducesResponseType(typeof(NotificationTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(NotificationTokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] NotificationTokenCreateRequest? request,
        CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return UnauthorizedError();
        }

        if (request is null)
        {
            return ValidationError("body", "Request body is required.");
        }

        var token = request.Token.Trim();
        if (token.Length == 0)
        {
            return ValidationError("token", "token is required.");
        }

        if (token.Length > 500)
        {
            return ValidationError("token", "token must be at most 500 characters.");
        }

        if (!TryParsePlatform(request.Platform, out var platform))
        {
            return ValidationError("platform", "platform must be one of: android, ios, web.");
        }

        var existingToken = await _dbContext.NotificationTokens
            .FirstOrDefaultAsync(notificationToken => notificationToken.Token == token, cancellationToken);

        if (existingToken is not null)
        {
            Update(existingToken, currentUser.Id, platform);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(existingToken));
        }

        var notificationToken = new NotificationToken
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.Id,
            Token = token,
            Platform = platform,
            CreatedAt = UtcNow()
        };

        _dbContext.NotificationTokens.Add(notificationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Created((string?)null, ToResponse(notificationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueTokenConflict(exception))
        {
            _dbContext.Entry(notificationToken).State = EntityState.Detached;

            var racedToken = await _dbContext.NotificationTokens
                .FirstOrDefaultAsync(existing => existing.Token == token, cancellationToken);

            if (racedToken is null)
            {
                return ConflictError();
            }

            Update(racedToken, currentUser.Id, platform);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse(racedToken));
            }
            catch (DbUpdateException retryException) when (IsUniqueTokenConflict(retryException))
            {
                return ConflictError();
            }
        }
    }

    [HttpDelete("{tokenId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string tokenId, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return UnauthorizedError();
        }

        if (!Guid.TryParse(tokenId, out var parsedTokenId))
        {
            return ValidationError("tokenId", "tokenId must be a valid GUID.");
        }

        var notificationToken = await _dbContext.NotificationTokens
            .FirstOrDefaultAsync(
                token => token.Id == parsedTokenId && token.UserId == currentUser.Id,
                cancellationToken);

        if (notificationToken is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Notification token was not found.",
                [new FieldError("tokenId", "Notification token was not found.")]));
        }

        _dbContext.NotificationTokens.Remove(notificationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<AppUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var result = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        return result is CurrentUserResult.Found found ? found.User : null;
    }

    private void Update(NotificationToken notificationToken, Guid userId, PlatformType platform)
    {
        notificationToken.UserId = userId;
        notificationToken.Platform = platform;
        notificationToken.UpdatedAt = UtcNow();
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private IActionResult UnauthorizedError() =>
        Unauthorized(CreateError("unauthorized", "Authentication required."));

    private IActionResult ValidationError(string field, string message) =>
        BadRequest(CreateError(
            "validation_error",
            "The request contains invalid fields.",
            [new FieldError(field, message)]));

    private IActionResult ConflictError() =>
        Conflict(CreateError(
            "conflict",
            "Notification token could not be registered.",
            [new FieldError("token", "The token was modified concurrently. Retry the request.")]));

    private ErrorResponse CreateError(
        string code,
        string message,
        IReadOnlyList<FieldError>? details = null) =>
        new()
        {
            Error = new ErrorBody
            {
                Code = code,
                Message = message,
                Details = details,
                RequestId = HttpContext.TraceIdentifier
            }
        };

    private static bool TryParsePlatform(string? value, out PlatformType platform)
    {
        platform = default;

        return value?.Trim().ToLowerInvariant() switch
        {
            "android" => SetPlatform(PlatformType.Android, out platform),
            "ios" => SetPlatform(PlatformType.Ios, out platform),
            "web" => SetPlatform(PlatformType.Web, out platform),
            _ => false
        };
    }

    private static bool SetPlatform(PlatformType value, out PlatformType platform)
    {
        platform = value;
        return true;
    }

    private static bool IsUniqueTokenConflict(DbUpdateException exception) =>
        exception.ToString().Contains(UniqueTokenIndex, StringComparison.OrdinalIgnoreCase);

    private static NotificationTokenResponse ToResponse(NotificationToken token) =>
        new(
            token.Id,
            token.Token,
            token.Platform.ToString().ToLowerInvariant(),
            token.CreatedAt,
            token.UpdatedAt);
}
