using Inventory.Api.Auth;
using Inventory.Api.Contracts.Common;
using Inventory.Api.Contracts.Imports;
using Inventory.Api.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ErrorBody = Inventory.Api.Contracts.Errors.ErrorBody;
using ErrorResponse = Inventory.Api.Contracts.Errors.ErrorResponse;
using FieldError = Inventory.Api.Contracts.Errors.FieldError;

namespace Inventory.Api.Controllers;

[ApiController]
[Authorize]
[Route("import-batches")]
public class ImportBatchesController : ControllerBase
{
    private readonly IAuthCurrentUserService _currentUserService;
    private readonly IProductCsvImportService _importService;

    public ImportBatchesController(
        IAuthCurrentUserService currentUserService,
        IProductCsvImportService importService)
    {
        _currentUserService = currentUserService;
        _importService = importService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<ImportBatchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] ImportBatchQueryParameters query,
        CancellationToken cancellationToken)
    {
        var result = await _importService.ListBatchesAsync(query, cancellationToken);

        return result switch
        {
            ListImportBatchesResult.Success success => Ok(success.Page),
            ListImportBatchesResult.ValidationFailed validation => BadRequest(ValidationError(validation.Details)),
            _ => throw new InvalidOperationException("Unhandled import batch list result.")
        };
    }

    [HttpPost]
    [ProducesResponseType(typeof(ImportBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateMetadataBatch(
        [FromBody] ImportBatchCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FileName))
        {
            return BadRequest(ValidationError(
                [new FieldError("fileName", "fileName is required.")]));
        }

        return BadRequest(CreateError(
            "csv_upload_required",
            "Use POST /import-batches/products with multipart/form-data to import products from CSV.",
            [new FieldError("file", "CSV upload is required for this import flow.")]));
    }

    [HttpPost("products")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImportBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ImportProducts(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var currentUser = await _currentUserService.GetCurrentUserAsync(User, cancellationToken);
        if (currentUser is not CurrentUserResult.Found found)
        {
            return Unauthorized(CreateError("unauthorized", "Authentication required."));
        }

        var result = await _importService.ImportProductsAsync(
            found.User.Id,
            file,
            cancellationToken);

        return result switch
        {
            ImportProductsResult.Success success => Created(
                $"/import-batches/{success.Batch.Id}",
                success.Batch),

            ImportProductsResult.ValidationFailed validation => BadRequest(ValidationError(validation.Details)),

            _ => throw new InvalidOperationException("Unhandled product CSV import result.")
        };
    }

    [HttpGet("{batchId}")]
    [ProducesResponseType(typeof(ImportBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        string batchId,
        CancellationToken cancellationToken)
    {
        var result = await _importService.GetBatchAsync(batchId, cancellationToken);

        return result switch
        {
            GetImportBatchResult.Success success => Ok(success.Batch),
            GetImportBatchResult.ValidationFailed validation => BadRequest(ValidationError(validation.Details)),
            GetImportBatchResult.NotFound => NotFound(CreateError("not_found", "Import batch not found.")),
            _ => throw new InvalidOperationException("Unhandled import batch detail result.")
        };
    }

    [HttpGet("{batchId}/errors")]
    [ProducesResponseType(typeof(PaginatedResponse<ImportBatchErrorResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListErrors(
        string batchId,
        [FromQuery] ImportBatchErrorQueryParameters query,
        CancellationToken cancellationToken)
    {
        var result = await _importService.ListBatchErrorsAsync(batchId, query, cancellationToken);

        return result switch
        {
            ListImportBatchErrorsResult.Success success => Ok(success.Page),
            ListImportBatchErrorsResult.ValidationFailed validation => BadRequest(ValidationError(validation.Details)),
            ListImportBatchErrorsResult.BatchNotFound => NotFound(CreateError("not_found", "Import batch not found.")),
            _ => throw new InvalidOperationException("Unhandled import batch errors result.")
        };
    }

    private ErrorResponse ValidationError(IReadOnlyList<FieldError> details) =>
        CreateError("validation_error", "The request contains invalid fields.", details);

    private ErrorResponse CreateError(
        string code,
        string message,
        IReadOnlyList<FieldError>? details = null) =>
        new(new ErrorBody(
            code,
            message,
            details ?? Array.Empty<FieldError>(),
            HttpContext.TraceIdentifier));
}
