using Inventory.Api.Contracts.Common;
using Inventory.Api.Contracts.Errors;
using Inventory.Api.Contracts.Products;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Inventory.Api.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const long MaxImageSize = 5 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> ImageExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp"
        };
    private static readonly Dictionary<string, string> UpdatableProductFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "name",
        ["sku"] = "sku",
        ["barcode"] = "barcode",
        ["category"] = "category",
        ["description"] = "description",
        ["imageUrl"] = "imageUrl",
        ["minStock"] = "minStock"
    };

    private readonly InventoryDbContext _dbContext;
    private readonly IProductImageStorage _productImageStorage;

    public ProductsController(
        InventoryDbContext dbContext,
        IProductImageStorage productImageStorage)
    {
        _dbContext = dbContext;
        _productImageStorage = productImageStorage;
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
        if (!TryParseProductId(productId, out var parsedProductId, out var invalidIdResult))
        {
            return invalidIdResult;
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

    [HttpPatch("{productId}")]
    public async Task<IActionResult> UpdateProduct(
        string productId,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (!TryParseProductId(productId, out var parsedProductId, out var invalidIdResult))
        {
            return invalidIdResult;
        }

        var (request, parseErrors) = ParseUpdateRequest(body);
        if (parseErrors.Count > 0)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                parseErrors));
        }

        if (!request.HasAnyUpdatableField)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                new[] { new FieldError("body", "At least one updatable field must be provided.") }));
        }

        var validationErrors = ValidateUpdateRequest(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validationErrors));
        }

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == parsedProductId, cancellationToken);

        if (product is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Product was not found.",
                new[] { new FieldError("productId", "Product was not found.") }));
        }

        var newSku = request.Sku.IsPresent ? request.Sku.Value!.Trim() : product.Sku;
        var newBarcode = request.Barcode.IsPresent ? TrimToNull(request.Barcode.Value) : product.Barcode;

        if (request.Sku.IsPresent &&
            await _dbContext.Products.AnyAsync(existing => existing.Id != product.Id && existing.Sku == newSku, cancellationToken))
        {
            return Conflict(CreateConflict("sku", "A product with this sku already exists.", "sku must be unique within the catalog."));
        }

        if (request.Barcode.IsPresent &&
            newBarcode is not null &&
            await _dbContext.Products.AnyAsync(existing => existing.Id != product.Id && existing.Barcode == newBarcode, cancellationToken))
        {
            return Conflict(CreateConflict("barcode", "A product with this barcode already exists.", "barcode must be unique within the catalog."));
        }

        ApplyUpdate(product, request);
        product.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception, "UX_products_sku"))
        {
            return Conflict(CreateConflict("sku", "A product with this sku already exists.", "sku must be unique within the catalog."));
        }
        catch (DbUpdateException exception) when (newBarcode is not null && IsUniqueConstraintViolation(exception, "UX_products_barcode_filtered"))
        {
            return Conflict(CreateConflict("barcode", "A product with this barcode already exists.", "barcode must be unique within the catalog."));
        }

        return Ok(ToResponse(product));
    }

    [HttpPatch("{productId}/deactivate")]
    public async Task<IActionResult> DeactivateProduct(string productId, CancellationToken cancellationToken)
    {
        if (!TryParseProductId(productId, out var parsedProductId, out var invalidIdResult))
        {
            return invalidIdResult;
        }

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == parsedProductId, cancellationToken);

        if (product is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Product was not found.",
                new[] { new FieldError("productId", "Product was not found.") }));
        }

        if (!product.IsActive)
        {
            return NoContent();
        }

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPatch("{productId}/activate")]
    public async Task<IActionResult> ActivateProduct(string productId, CancellationToken cancellationToken)
    {
        if (!TryParseProductId(productId, out var parsedProductId, out var invalidIdResult))
        {
            return invalidIdResult;
        }

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == parsedProductId, cancellationToken);

        if (product is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Product was not found.",
                new[] { new FieldError("productId", "Product was not found.") }));
        }

        if (product.IsActive)
        {
            return NoContent();
        }

        product.IsActive = true;
        product.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{productId}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadProductImage(
        string productId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryParseProductId(productId, out var parsedProductId, out var invalidIdResult))
        {
            return invalidIdResult;
        }

        var validationErrors = ValidateImage(file);
        if (validationErrors.Count > 0)
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                validationErrors));
        }

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == parsedProductId, cancellationToken);

        if (product is null)
        {
            return NotFound(CreateError(
                "not_found",
                "Product was not found.",
                new[] { new FieldError("productId", "Product was not found.") }));
        }

        var previousImageUrl = product.ImageUrl;
        string newImageUrl;

        await using (var content = file!.OpenReadStream())
        {
            newImageUrl = await _productImageStorage.SaveAsync(
                product.Id,
                content,
                ImageExtensionsByContentType[file.ContentType],
                cancellationToken);
        }

        product.ImageUrl = newImageUrl;
        product.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _productImageStorage.DeleteIfManaged(newImageUrl);
            throw;
        }

        _productImageStorage.DeleteIfManaged(previousImageUrl);

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
            AsUtc(product.CreatedAt),
            product.UpdatedAt.HasValue ? AsUtc(product.UpdatedAt.Value) : null);

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

    private static List<FieldError> ValidateImage(IFormFile? file)
    {
        var errors = new List<FieldError>();

        if (file is null)
        {
            errors.Add(new FieldError("file", "file is required."));
            return errors;
        }

        if (file.Length == 0)
        {
            errors.Add(new FieldError("file", "file must not be empty."));
        }
        else if (file.Length > MaxImageSize)
        {
            errors.Add(new FieldError("file", "file must not exceed 5 MB."));
        }

        if (!ImageExtensionsByContentType.ContainsKey(file.ContentType))
        {
            errors.Add(new FieldError("file", "file must be a JPEG, PNG, or WebP image."));
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

    private static (ProductUpdateRequest Request, List<FieldError> Errors) ParseUpdateRequest(JsonElement body)
    {
        var errors = new List<FieldError>();
        var request = new ProductUpdateRequest(
            PatchField<string>.Missing,
            PatchField<string>.Missing,
            PatchField<string>.Missing,
            PatchField<string>.Missing,
            PatchField<string>.Missing,
            PatchField<string>.Missing,
            PatchField<int>.Missing);

        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            errors.Add(new FieldError("body", "Request body is required."));
            return (request, errors);
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new FieldError("body", "Request body must be a JSON object."));
            return (request, errors);
        }

        PatchField<string> name = PatchField<string>.Missing;
        PatchField<string> sku = PatchField<string>.Missing;
        PatchField<string> barcode = PatchField<string>.Missing;
        PatchField<string> category = PatchField<string>.Missing;
        PatchField<string> description = PatchField<string>.Missing;
        PatchField<string> imageUrl = PatchField<string>.Missing;
        PatchField<int> minStock = PatchField<int>.Missing;

        foreach (var property in body.EnumerateObject())
        {
            if (!UpdatableProductFields.TryGetValue(property.Name, out var fieldName))
            {
                errors.Add(new FieldError(property.Name, $"{property.Name} is not an updatable field."));
                continue;
            }

            switch (fieldName)
            {
                case "name":
                    name = ReadOptionalString(property, fieldName, errors);
                    break;
                case "sku":
                    sku = ReadOptionalString(property, fieldName, errors);
                    break;
                case "barcode":
                    barcode = ReadOptionalString(property, fieldName, errors);
                    break;
                case "category":
                    category = ReadOptionalString(property, fieldName, errors);
                    break;
                case "description":
                    description = ReadOptionalString(property, fieldName, errors);
                    break;
                case "imageUrl":
                    imageUrl = ReadOptionalString(property, fieldName, errors);
                    break;
                case "minStock":
                    minStock = ReadOptionalInt(property, fieldName, errors);
                    break;
            }
        }

        return (new ProductUpdateRequest(name, sku, barcode, category, description, imageUrl, minStock), errors);
    }

    private static PatchField<string> ReadOptionalString(JsonProperty property, string fieldName, List<FieldError> errors)
    {
        if (property.Value.ValueKind == JsonValueKind.Null)
        {
            return PatchField<string>.Present(null);
        }

        if (property.Value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new FieldError(fieldName, $"{fieldName} must be a string."));
            return PatchField<string>.Missing;
        }

        return PatchField<string>.Present(property.Value.GetString());
    }

    private static PatchField<int> ReadOptionalInt(JsonProperty property, string fieldName, List<FieldError> errors)
    {
        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value))
        {
            errors.Add(new FieldError(fieldName, $"{fieldName} must be an integer."));
            return PatchField<int>.Missing;
        }

        return PatchField<int>.Present(value);
    }

    private static List<FieldError> ValidateUpdateRequest(ProductUpdateRequest request)
    {
        var errors = new List<FieldError>();

        if (request.Name.IsPresent)
        {
            ValidateRequiredText(request.Name.Value, "name", "Name is required.", 150, errors);
        }

        if (request.Sku.IsPresent)
        {
            ValidateRequiredText(request.Sku.Value, "sku", "Sku is required.", 100, errors);
        }

        if (request.Category.IsPresent)
        {
            ValidateRequiredText(request.Category.Value, "category", "Category is required.", 100, errors);
        }

        if (request.Barcode.IsPresent)
        {
            ValidateOptionalText(request.Barcode.Value, "barcode", 32, errors);
        }

        if (request.Description.IsPresent)
        {
            ValidateOptionalText(request.Description.Value, "description", 500, errors);
        }

        if (request.ImageUrl.IsPresent)
        {
            ValidateOptionalText(request.ImageUrl.Value, "imageUrl", 1000, errors);
            var imageUrl = TrimToNull(request.ImageUrl.Value);
            if (imageUrl is not null &&
                !Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            {
                errors.Add(new FieldError("imageUrl", "imageUrl must be a valid URI."));
            }
        }

        if (request.MinStock.IsPresent && request.MinStock.Value < 0)
        {
            errors.Add(new FieldError("minStock", "minStock must be greater than or equal to 0."));
        }

        return errors;
    }

    private static void ApplyUpdate(Product product, ProductUpdateRequest request)
    {
        if (request.Name.IsPresent)
        {
            product.Name = request.Name.Value!.Trim();
        }

        if (request.Sku.IsPresent)
        {
            product.Sku = request.Sku.Value!.Trim();
        }

        if (request.Barcode.IsPresent)
        {
            product.Barcode = TrimToNull(request.Barcode.Value);
        }

        if (request.Category.IsPresent)
        {
            product.Category = request.Category.Value!.Trim();
        }

        if (request.Description.IsPresent)
        {
            product.Description = TrimToNull(request.Description.Value);
        }

        if (request.ImageUrl.IsPresent)
        {
            product.ImageUrl = TrimToNull(request.ImageUrl.Value);
        }

        if (request.MinStock.IsPresent)
        {
            product.MinStock = request.MinStock.Value;
        }
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

    private bool TryParseProductId(string productId, out Guid parsedProductId, out IActionResult invalidIdResult)
    {
        if (Guid.TryParse(productId, out parsedProductId))
        {
            invalidIdResult = null!;
            return true;
        }

        invalidIdResult = BadRequest(CreateError(
            "validation_error",
            "The request contains invalid fields.",
            new[] { new FieldError("productId", "productId must be a valid GUID.") }));
        return false;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException?.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase) == true;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
