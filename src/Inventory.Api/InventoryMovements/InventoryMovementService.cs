using Inventory.Api.Common.Errors;
using Inventory.Api.Common.Pagination;
using Inventory.Api.Data;
using Inventory.Api.InventoryMovements.Dtos;
using Inventory.Api.Models;
using Inventory.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.InventoryMovements;

public class InventoryMovementService : IInventoryMovementService
{
    private const int MaxPageSize = 100;

    private readonly InventoryDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IStockAlertNotificationService _stockAlertNotificationService;

    public InventoryMovementService(
        InventoryDbContext db,
        TimeProvider timeProvider,
        IStockAlertNotificationService stockAlertNotificationService)
    {
        _db = db;
        _timeProvider = timeProvider;
        _stockAlertNotificationService = stockAlertNotificationService;
    }

    public async Task<CreateInventoryMovementResult> CreateAsync(
        Guid userId,
        InventoryMovementCreateRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateCreateRequest(request, out var productId, out var branchId);
        if (validationErrors.Count > 0)
        {
            return new CreateInventoryMovementResult.ValidationFailed(validationErrors);
        }

        var type = request.Type!.Value;
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
        {
            return new CreateInventoryMovementResult.ProductNotFound();
        }

        if (!product.IsActive)
        {
            return new CreateInventoryMovementResult.InactiveProduct();
        }

        var branch = await _db.Branches
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);

        if (branch is null)
        {
            return new CreateInventoryMovementResult.BranchNotFound();
        }

        if (!branch.IsActive)
        {
            return new CreateInventoryMovementResult.InactiveBranch();
        }

        var stock = await _db.Stocks
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.BranchId == branchId, cancellationToken);

        if (stock is null)
        {
            return new CreateInventoryMovementResult.StockNotFound();
        }

        var previousStock = stock.AvailableQuantity;
        if (type == MovementType.Outgoing && request.Quantity > previousStock)
        {
            return new CreateInventoryMovementResult.InsufficientStock(
                request.Quantity,
                previousStock);
        }

        var resultingStock = type switch
        {
            MovementType.Incoming => previousStock + request.Quantity,
            MovementType.Outgoing => previousStock - request.Quantity,
            _ => throw new InvalidOperationException("Unsupported movement type was not validated.")
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            BranchId = branchId,
            UserId = userId,
            Type = type,
            Quantity = request.Quantity,
            PreviousStock = previousStock,
            ResultingStock = resultingStock,
            Reason = request.Reason!.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = nowUtc
        };

        stock.AvailableQuantity = resultingStock;
        stock.LastMovementId = movement.Id;
        stock.LastMovementAt = nowUtc;
        stock.UpdatedAt = nowUtc;

        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _stockAlertNotificationService.NotifyIfSeverityIncreasedAsync(
            previousStock,
            resultingStock,
            stock.MinStock,
            product.Id,
            product.Name,
            branch.Id,
            branch.Name,
            cancellationToken);

        return new CreateInventoryMovementResult.Success(
            InventoryMovementResponse.FromEntity(movement));
    }

    public async Task<ListInventoryMovementsResult> ListAsync(
        InventoryMovementQueryParameters query,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateListQuery(
            query,
            out var productId,
            out var branchId,
            out var userId,
            out var page,
            out var pageSize);

        if (validationErrors.Count > 0)
        {
            return new ListInventoryMovementsResult.ValidationFailed(validationErrors);
        }

        var movements = _db.InventoryMovements.AsNoTracking().AsQueryable();

        if (productId.HasValue)
        {
            movements = movements.Where(m => m.ProductId == productId.Value);
        }

        if (branchId.HasValue)
        {
            movements = movements.Where(m => m.BranchId == branchId.Value);
        }

        if (query.Type.HasValue)
        {
            movements = movements.Where(m => m.Type == query.Type.Value);
        }

        if (userId.HasValue)
        {
            movements = movements.Where(m => m.UserId == userId.Value);
        }

        if (query.From.HasValue)
        {
            movements = movements.Where(m => m.CreatedAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            movements = movements.Where(m => m.CreatedAt <= query.To.Value);
        }

        var total = await movements.CountAsync(cancellationToken);
        var movementItems = await movements
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = new PaginatedResponse<InventoryMovementResponse>
        {
            Items = movementItems
                .Select(InventoryMovementResponse.FromEntity)
                .ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < total
        };

        return new ListInventoryMovementsResult.Success(response);
    }

    public async Task<GetInventoryMovementResult> GetByIdAsync(
        string movementId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(movementId, out var parsedMovementId))
        {
            return new GetInventoryMovementResult.ValidationFailed(
                [new FieldError("movementId", "Movement id must be a valid GUID.")]);
        }

        var movement = await _db.InventoryMovements
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == parsedMovementId, cancellationToken);

        if (movement is null)
        {
            return new GetInventoryMovementResult.NotFound();
        }

        return new GetInventoryMovementResult.Success(
            InventoryMovementResponse.FromEntity(movement));
    }

    private static List<FieldError> ValidateCreateRequest(
        InventoryMovementCreateRequest request,
        out Guid productId,
        out Guid branchId)
    {
        var errors = new List<FieldError>();
        productId = Guid.Empty;
        branchId = Guid.Empty;

        if (!Guid.TryParse(request.ProductId, out productId))
        {
            errors.Add(new FieldError("productId", "Product id must be a valid GUID."));
        }

        if (!Guid.TryParse(request.BranchId, out branchId))
        {
            errors.Add(new FieldError("branchId", "Branch id must be a valid GUID."));
        }

        if (request.Type is MovementType.Adjustment)
        {
            errors.Add(new FieldError("type", "Adjustment movements are not supported."));
        }

        if (request.Type is not (MovementType.Incoming or MovementType.Outgoing))
        {
            errors.Add(new FieldError("type", "Movement type must be incoming or outgoing."));
        }

        if (request.Quantity <= 0)
        {
            errors.Add(new FieldError("quantity", "Quantity must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add(new FieldError("reason", "Reason is required."));
        }
        else if (request.Reason.Trim().Length > 200)
        {
            errors.Add(new FieldError("reason", "Reason cannot exceed 200 characters."));
        }

        if (request.Notes?.Trim().Length > 500)
        {
            errors.Add(new FieldError("notes", "Notes cannot exceed 500 characters."));
        }

        return errors;
    }

    private static List<FieldError> ValidateListQuery(
        InventoryMovementQueryParameters query,
        out Guid? productId,
        out Guid? branchId,
        out Guid? userId,
        out int page,
        out int pageSize)
    {
        var errors = new List<FieldError>();
        productId = ParseOptionalGuid(query.ProductId, "productId", errors);
        branchId = ParseOptionalGuid(query.BranchId, "branchId", errors);
        userId = ParseOptionalGuid(query.UserId, "userId", errors);
        page = query.Page;
        pageSize = query.PageSize;

        if (page < 1)
        {
            errors.Add(new FieldError("page", "Page must be greater than or equal to 1."));
            page = 1;
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors.Add(new FieldError("pageSize", $"Page size must be between 1 and {MaxPageSize}."));
            pageSize = 20;
        }

        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
        {
            errors.Add(new FieldError("from", "From must be earlier than or equal to to."));
        }

        return errors;
    }

    private static Guid? ParseOptionalGuid(
        string? value,
        string fieldName,
        List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new FieldError(fieldName, $"{fieldName} must be a valid GUID."));
        return null;
    }
}
