using Inventory.Api.Common.Errors;
using Inventory.Api.Contracts.Products;
using Inventory.Api.ProductLookup;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("product-lookup")]
[Produces("application/json")]
public class ProductLookupController : ControllerBase
{
    private readonly IExternalProductLookupService _lookupService;

    public ProductLookupController(IExternalProductLookupService lookupService)
    {
        _lookupService = lookupService;
    }

    [HttpGet("{barcode}")]
    [ProducesResponseType(typeof(ExternalProductSuggestion), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LookupProduct(
        string barcode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(CreateError(
                "validation_error",
                "The request contains invalid fields.",
                [new FieldError("barcode", "Barcode is required.")]));
        }

        var trimmedBarcode = barcode.Trim();
        var result = await _lookupService.LookupAsync(trimmedBarcode, cancellationToken);

        return result switch
        {
            ExternalProductLookupResult.Success success => Ok(success.Suggestion),

            ExternalProductLookupResult.NotFound => NotFound(CreateError(
                "product_not_found",
                "No suggestion was found for the requested barcode.",
                [new FieldError("barcode", "Barcode not present in the upstream catalog.")])),

            ExternalProductLookupResult.ProviderUnavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                CreateError(
                    "service_unavailable",
                    "The upstream product provider is currently unavailable.")),

            _ => throw new InvalidOperationException("Unhandled product lookup result.")
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
