using Inventory.Api.Common.Errors;
using Inventory.Api.Common.Pagination;
using Inventory.Api.InventoryMovements.Dtos;

namespace Inventory.Api.InventoryMovements;

public interface IInventoryMovementService
{
    Task<CreateInventoryMovementResult> CreateAsync(
        Guid userId,
        InventoryMovementCreateRequest request,
        CancellationToken cancellationToken);

    Task<ListInventoryMovementsResult> ListAsync(
        InventoryMovementQueryParameters query,
        CancellationToken cancellationToken);

    Task<GetInventoryMovementResult> GetByIdAsync(
        string movementId,
        CancellationToken cancellationToken);
}

public abstract record CreateInventoryMovementResult
{
    public sealed record Success(InventoryMovementResponse Movement) : CreateInventoryMovementResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : CreateInventoryMovementResult;
    public sealed record ProductNotFound : CreateInventoryMovementResult;
    public sealed record BranchNotFound : CreateInventoryMovementResult;
    public sealed record StockNotFound : CreateInventoryMovementResult;
    public sealed record InactiveProduct : CreateInventoryMovementResult;
    public sealed record InactiveBranch : CreateInventoryMovementResult;
    public sealed record InsufficientStock(int RequestedQuantity, int AvailableQuantity) : CreateInventoryMovementResult;
}

public abstract record ListInventoryMovementsResult
{
    public sealed record Success(PaginatedResponse<InventoryMovementResponse> Page) : ListInventoryMovementsResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : ListInventoryMovementsResult;
}

public abstract record GetInventoryMovementResult
{
    public sealed record Success(InventoryMovementResponse Movement) : GetInventoryMovementResult;
    public sealed record ValidationFailed(IReadOnlyList<FieldError> Details) : GetInventoryMovementResult;
    public sealed record NotFound : GetInventoryMovementResult;
}
