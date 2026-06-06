using System.Text.Json;

namespace Inventory.Api.Common.Errors;

public static class AuthChallengeResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task WriteUnauthorizedAsync(HttpResponse response, string? requestId, CancellationToken cancellationToken = default)
        => WriteAsync(
            response,
            StatusCodes.Status401Unauthorized,
            "unauthorized",
            "Authentication required.",
            requestId,
            cancellationToken);

    public static Task WriteForbiddenAsync(HttpResponse response, string? requestId, CancellationToken cancellationToken = default)
        => WriteAsync(
            response,
            StatusCodes.Status403Forbidden,
            "forbidden",
            "You are not authorized to perform this action.",
            requestId,
            cancellationToken);

    private static Task WriteAsync(
        HttpResponse response,
        int statusCode,
        string code,
        string message,
        string? requestId,
        CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var body = new ErrorResponse
        {
            Error = new ErrorBody
            {
                Code = code,
                Message = message,
                RequestId = requestId
            }
        };

        return JsonSerializer.SerializeAsync(response.Body, body, SerializerOptions, cancellationToken);
    }
}
