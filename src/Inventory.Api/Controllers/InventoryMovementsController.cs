using Inventory.Api.Auth;
using Inventory.Api.Common.Errors;
using Inventory.Api.Common.Pagination;
using Inventory.Api.InventoryMovements;
using Inventory.Api.InventoryMovements.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("inventory-movements")]
public class InventoryMovementsController : ControllerBase
{
    private readonly IAuthCurrentUserService _currentUserService;
    private readonly IInventoryMovementService _movementService;

    public InventoryMovementsController(
        IAuthCurrentUserService currentUserService,
        IInventoryMovementService movementService)
    {
        _currentUserService = currentUserService;
        _movementService = movementService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<InventoryMovementResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] InventoryMovementQueryParameters query,
        CancellationToken cancellationToken)
    {
        var result = await _movementService.ListAsync(query, cancellationToken);

        return result switch
        {
            ListInventoryMovementsResult.Success success => Ok(success.Page),

            ListInventoryMovementsResult.ValidationFailed validation => BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validation.Details)),

            _ => throw new InvalidOperationException("Unhandled inventory movement list result.")
        };
    }

    [HttpPost]
    [ProducesResponseType(typeof(InventoryMovementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] InventoryMovementCreateRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        if (currentUser is not CurrentUserResult.Found found)
        {
            return Unauthorized(CreateError("unauthorized", "Authentication required."));
        }

        var result = await _movementService.CreateAsync(
            found.User.Id,
            request,
            cancellationToken);

        return result switch
        {
            CreateInventoryMovementResult.Success success => Created(
                $"/inventory-movements/{success.Movement.Id}",
                success.Movement),

            CreateInventoryMovementResult.ValidationFailed validation => BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validation.Details)),

            CreateInventoryMovementResult.ProductNotFound => NotFound(CreateError(
                "not_found",
                "Product not found.",
                [new FieldError("productId", "Product was not found.")])),

            CreateInventoryMovementResult.BranchNotFound => NotFound(CreateError(
                "not_found",
                "Branch not found.",
                [new FieldError("branchId", "Branch was not found.")])),

            CreateInventoryMovementResult.StockNotFound => NotFound(CreateError(
                "not_found",
                "Stock record not found.",
                [new FieldError("stock", "Stock record was not found for the selected product and branch.")])),

            CreateInventoryMovementResult.InactiveProduct => UnprocessableEntity(CreateError(
                "inactive_product",
                "Product is inactive.",
                [new FieldError("productId", "Inactive products cannot receive inventory movements.")])),

            CreateInventoryMovementResult.InactiveBranch => UnprocessableEntity(CreateError(
                "inactive_branch",
                "Branch is inactive.",
                [new FieldError("branchId", "Inactive branches cannot receive inventory movements.")])),

            CreateInventoryMovementResult.InsufficientStock insufficient => UnprocessableEntity(CreateError(
                "insufficient_stock",
                "The requested quantity exceeds the available stock.",
                [new FieldError(
                    "quantity",
                    $"Requested {insufficient.RequestedQuantity} but only {insufficient.AvailableQuantity} available at the selected branch.")])),

            _ => throw new InvalidOperationException("Unhandled inventory movement creation result.")
        };
    }

    [HttpGet("{movementId}")]
    [ProducesResponseType(typeof(InventoryMovementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        string movementId,
        CancellationToken cancellationToken)
    {
        var result = await _movementService.GetByIdAsync(movementId, cancellationToken);

        return result switch
        {
            GetInventoryMovementResult.Success success => Ok(success.Movement),

            GetInventoryMovementResult.ValidationFailed validation => BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validation.Details)),

            GetInventoryMovementResult.NotFound => NotFound(CreateError(
                "not_found",
                "Inventory movement not found.")),

            _ => throw new InvalidOperationException("Unhandled inventory movement detail result.")
        };
    }

    private ErrorResponse CreateError(
        string code,
        string message,
        IReadOnlyList<FieldError>? details = null) => new()
    {
        Error = new ErrorBody
        {
            Code = code,
            Message = message,
            Details = details,
            RequestId = HttpContext.TraceIdentifier
        }
    };
}
