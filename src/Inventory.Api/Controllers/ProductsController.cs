using Inventory.Api.Contracts.Common;
using Inventory.Api.Contracts.Errors;
using Inventory.Api.Contracts.Products;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly InventoryDbContext _dbContext;

    public ProductsController(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] bool? isActive,
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] int page = DefaultPage,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidatePagination(page, pageSize);
        if (validationErrors.Count > 0)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validationErrors));
        }

        var query = _dbContext.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(product =>
                product.Name.Contains(q) ||
                product.Sku.Contains(q) ||
                (product.Barcode != null && product.Barcode.Contains(q)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(product => product.Category == category);
        }

        if (isActive.HasValue)
        {
            query = query.Where(product => product.IsActive == isActive.Value);
        }

        if (lowStockOnly)
        {
            // A product is low stock only when at least one branch stock row exists at or below the product minimum.
            query = query.Where(product => product.Stocks.Any(stock => stock.AvailableQuantity <= product.MinStock));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => ToResponse(product))
            .ToListAsync(cancellationToken);

        return Ok(new PaginatedResponse<ProductResponse>(
            items,
            total,
            page,
            pageSize,
            page * pageSize < total));
    }

    [HttpPost]
    public async Task<IActionResult> CreateProduct(
        [FromBody] ProductCreateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                new[] { new FieldError("body", "Request body is required.") }));
        }

        var validationErrors = ValidateCreateRequest(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validationErrors));
        }

        var sku = request.Sku!.Trim();
        var barcode = TrimToNull(request.Barcode);

        if (await _dbContext.Products.AnyAsync(product => product.Sku == sku, cancellationToken))
        {
            return Conflict(CreateConflict("sku", "A product with this sku already exists.", "sku must be unique within the catalog."));
        }

        if (barcode is not null &&
            await _dbContext.Products.AnyAsync(product => product.Barcode == barcode, cancellationToken))
        {
            return Conflict(CreateConflict("barcode", "A product with this barcode already exists.", "barcode must be unique within the catalog."));
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            Sku = sku,
            Barcode = barcode,
            Category = request.Category!.Trim(),
            Description = TrimToNull(request.Description),
            ImageUrl = TrimToNull(request.ImageUrl),
            MinStock = request.MinStock!.Value,
            IsActive = request.IsActive ?? true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null
        };

        _dbContext.Products.Add(product);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception, "UX_products_sku"))
        {
            return Conflict(CreateConflict("sku", "A product with this sku already exists.", "sku must be unique within the catalog."));
        }
        catch (DbUpdateException exception) when (barcode is not null && IsUniqueConstraintViolation(exception, "UX_products_barcode_filtered"))
        {
            return Conflict(CreateConflict("barcode", "A product with this barcode already exists.", "barcode must be unique within the catalog."));
        }

        return CreatedAtAction(nameof(GetProductById), new { productId = product.Id }, ToResponse(product));
    }

    [HttpGet("{productId}")]
    public async Task<IActionResult> GetProductById(string productId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(productId, out var parsedProductId))
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                new[] { new FieldError("productId", "productId must be a valid GUID.") }));
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == parsedProductId, cancellationToken);

        if (product is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Product was not found.",
                new[] { new FieldError("productId", "Product was not found.") }));
        }

        return Ok(ToResponse(product));
    }

    private static ProductResponse ToResponse(Product product) =>
        new(
            product.Id,
            product.Name,
            product.Sku,
            product.Barcode,
            product.Category,
            product.Description,
            product.ImageUrl,
            product.MinStock,
            product.IsActive,
            product.CreatedAt,
            product.UpdatedAt);

    private static List<FieldError> ValidatePagination(int page, int pageSize)
    {
        var errors = new List<FieldError>();

        if (page < 1)
        {
            errors.Add(new FieldError("page", "page must be greater than or equal to 1."));
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors.Add(new FieldError("pageSize", "pageSize must be between 1 and 100."));
        }

        return errors;
    }

    private static List<FieldError> ValidateCreateRequest(ProductCreateRequest request)
    {
        var errors = new List<FieldError>();

        ValidateRequiredText(request.Name, "name", "Name is required.", 150, errors);
        ValidateRequiredText(request.Sku, "sku", "Sku is required.", 100, errors);
        ValidateRequiredText(request.Category, "category", "Category is required.", 100, errors);
        ValidateOptionalText(request.Barcode, "barcode", 32, errors);
        ValidateOptionalText(request.Description, "description", 500, errors);
        ValidateOptionalText(request.ImageUrl, "imageUrl", 1000, errors);

        if (!request.MinStock.HasValue)
        {
            errors.Add(new FieldError("minStock", "minStock is required."));
        }
        else if (request.MinStock.Value < 0)
        {
            errors.Add(new FieldError("minStock", "minStock must be greater than or equal to 0."));
        }

        var imageUrl = TrimToNull(request.ImageUrl);
        if (imageUrl is not null &&
            !Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            errors.Add(new FieldError("imageUrl", "imageUrl must be a valid URI."));
        }

        return errors;
    }

    private static void ValidateRequiredText(
        string? value,
        string field,
        string requiredMessage,
        int maxLength,
        List<FieldError> errors)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add(new FieldError(field, requiredMessage));
            return;
        }

        if (trimmed.Length > maxLength)
        {
            errors.Add(new FieldError(field, $"{field} must be at most {maxLength} characters."));
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string field,
        int maxLength,
        List<FieldError> errors)
    {
        var trimmed = value?.Trim();
        if (trimmed is not null && trimmed.Length > maxLength)
        {
            errors.Add(new FieldError(field, $"{field} must be at most {maxLength} characters."));
        }
    }

    private ErrorResponse CreateConflict(string field, string message, string detailMessage) =>
        CreateError(
            "conflict",
            message,
            new[] { new FieldError(field, detailMessage) });

    private ErrorResponse CreateError(string code, string message, IReadOnlyList<FieldError> details) =>
        new(new ErrorBody(code, message, details, HttpContext.TraceIdentifier));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException?.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase) == true;
}
