using Inventory.Api.Contracts;
using Inventory.Api.Data;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("stock")]
[Produces("application/json")]
public class StockController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;

    public StockController(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StockResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStock(
        [FromQuery] string? branchId,
        [FromQuery] string? productId,
        CancellationToken cancellationToken)
    {
        if (!TryParseOptionalGuid(branchId, "branchId", out var parsedBranchId, out var branchError))
        {
            return BadRequest(branchError);
        }

        if (!TryParseOptionalGuid(productId, "productId", out var parsedProductId, out var productError))
        {
            return BadRequest(productError);
        }

        var query = _dbContext.Stocks
            .AsNoTracking()
            .Include(stock => stock.Product)
            .Include(stock => stock.Branch)
            .AsQueryable();

        if (parsedBranchId.HasValue)
        {
            query = query.Where(stock => stock.BranchId == parsedBranchId.Value);
        }

        if (parsedProductId.HasValue)
        {
            query = query.Where(stock => stock.ProductId == parsedProductId.Value);
        }

        var stocks = await query
            .OrderBy(stock => stock.Branch.Name)
            .ThenBy(stock => stock.Product.Name)
            .ToListAsync(cancellationToken);

        return Ok(stocks.Select(ToResponse).ToList());
    }

    [HttpGet("lookup")]
    [ProducesResponseType(typeof(StockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LookupStock(
        [FromQuery] string? productId,
        [FromQuery] string? branchId,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(productId, "productId", out var parsedProductId, out var productError))
        {
            return BadRequest(productError);
        }

        if (!TryParseRequiredGuid(branchId, "branchId", out var parsedBranchId, out var branchError))
        {
            return BadRequest(branchError);
        }

        var stock = await _dbContext.Stocks
            .AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(
                s => s.ProductId == parsedProductId && s.BranchId == parsedBranchId,
                cancellationToken);

        if (stock is null)
        {
            return NotFound(new ErrorResponse(
                "stock_not_found",
                "Stock was not found for the requested product and branch."));
        }

        return Ok(ToResponse(stock));
    }

    [HttpGet("{stockId}")]
    [ProducesResponseType(typeof(StockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStockById(
        string stockId,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(stockId, "stockId", out var parsedStockId, out var stockError))
        {
            return BadRequest(stockError);
        }

        var stock = await _dbContext.Stocks
            .AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == parsedStockId, cancellationToken);

        if (stock is null)
        {
            return NotFound(new ErrorResponse(
                "stock_not_found",
                "Stock was not found."));
        }

        return Ok(ToResponse(stock));
    }

    private static bool TryParseOptionalGuid(
        string? value,
        string field,
        out Guid? parsedValue,
        out ErrorResponse? error)
    {
        parsedValue = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out var guid))
        {
            parsedValue = guid;
            return true;
        }

        error = InvalidGuidError(field);
        return false;
    }

    private static bool TryParseRequiredGuid(
        string? value,
        string field,
        out Guid parsedValue,
        out ErrorResponse? error)
    {
        parsedValue = Guid.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = new ErrorResponse(
                "missing_required_parameter",
                $"{field} is required.");
            return false;
        }

        if (Guid.TryParse(value, out parsedValue))
        {
            return true;
        }

        error = InvalidGuidError(field);
        return false;
    }

    private static ErrorResponse InvalidGuidError(string field) =>
        new(
            "invalid_guid",
            $"{field} must be a valid GUID.");

    private static StockResponse ToResponse(Stock stock) =>
        new(
            stock.Id,
            stock.AvailableQuantity,
            stock.MinStock,
            stock.AvailableQuantity <= stock.MinStock,
            stock.LastMovementAt,
            stock.UpdatedAt,
            new StockProductResponse(
                stock.Product.Id,
                stock.Product.Name,
                stock.Product.Sku,
                stock.Product.Barcode,
                stock.Product.Category,
                stock.Product.ImageUrl),
            new StockBranchResponse(
                stock.Branch.Id,
                stock.Branch.Name,
                stock.Branch.Address));
}
