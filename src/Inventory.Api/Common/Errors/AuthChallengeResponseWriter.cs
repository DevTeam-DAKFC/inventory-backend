using System.Text.Json;

namespace Inventory.Api.Common.Errors;

public static class AuthChallengeResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Task WriteUnauthorizedAsync(HttpResponse response, string? requestId, CancellationToken cancellationToken = default)
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.ContentType = "application/json";

        var body = new ErrorResponse
        {
            Error = new ErrorBody
            {
                Code = "unauthorized",
                Message = "Authentication required.",
                RequestId = requestId
            }
        };

        return JsonSerializer.SerializeAsync(response.Body, body, SerializerOptions, cancellationToken);
    }
}
