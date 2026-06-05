using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("branches")]
public class BranchesController : ControllerBase
{
    private readonly InventoryDbContext _dbContext;

    public BranchesController(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BranchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<BranchResponse>>> GetBranches([FromQuery] bool? active = null)
    {
        var isAdmin = User.IsInRole(UserRole.Admin.ToString());
        var effectiveActiveFilter = isAdmin ? active ?? true : true;

        var branches = await _dbContext.Branches
            .AsNoTracking()
            .Where(branch => branch.IsActive == effectiveActiveFilter)
            .OrderBy(branch => branch.Name)
            .Select(branch => ToResponse(branch))
            .ToListAsync();

        return Ok(branches);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BranchResponse>> CreateBranch([FromBody] BranchCreateRequest request)
    {
        var validationErrors = ValidateCreateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ValidationError(validationErrors);
        }

        var now = DateTime.UtcNow;
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            Address = NormalizeOptionalText(request.Address),
            IsActive = true,
            CreatedAt = now
        };

        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();

        var response = ToResponse(branch);
        return CreatedAtAction(nameof(GetBranch), new { branchId = branch.Id }, response);
    }

    [HttpGet("{branchId:guid}")]
    [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BranchResponse>> GetBranch(Guid branchId)
    {
        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(branch => branch.Id == branchId);

        if (branch is null || (!branch.IsActive && !User.IsInRole(UserRole.Admin.ToString())))
        {
            return NotFoundError("Branch not found.");
        }

        return Ok(ToResponse(branch));
    }

    [HttpPatch("{branchId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BranchResponse>> UpdateBranch(Guid branchId, [FromBody] BranchUpdateRequest request)
    {
        var validationErrors = ValidateUpdateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ValidationError(validationErrors);
        }

        var branch = await _dbContext.Branches.FirstOrDefaultAsync(branch => branch.Id == branchId);
        if (branch is null)
        {
            return NotFoundError("Branch not found.");
        }

        if (request.Name is not null)
        {
            branch.Name = request.Name.Trim();
        }

        if (request.Address is not null)
        {
            branch.Address = NormalizeOptionalText(request.Address);
        }

        branch.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(branch));
    }

    [HttpPatch("{branchId:guid}/deactivate")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BranchResponse>> DeactivateBranch(Guid branchId)
    {
        var branch = await _dbContext.Branches.FirstOrDefaultAsync(branch => branch.Id == branchId);
        if (branch is null)
        {
            return NotFoundError("Branch not found.");
        }

        branch.IsActive = false;
        branch.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(branch));
    }

    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id,
        branch.Name,
        branch.Address,
        branch.IsActive,
        branch.CreatedAt,
        branch.UpdatedAt);

    private static Dictionary<string, string[]> ValidateCreateRequest(BranchCreateRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["The name field is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateRequest(BranchUpdateRequest? request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request is null)
        {
            errors["body"] = ["The request body is required."];
            return errors;
        }

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["The name field cannot be blank."];
        }

        return errors;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private BadRequestObjectResult ValidationError(IDictionary<string, string[]> errors) =>
        BadRequest(new ErrorResponse("validation_error", "One or more validation errors occurred.", errors));

    private NotFoundObjectResult NotFoundError(string message) =>
        NotFound(new ErrorResponse("not_found", message));
}
