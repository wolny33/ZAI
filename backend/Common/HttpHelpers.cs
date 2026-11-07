using System.Text.Json;

namespace Backend.Common
{

    /// <summary>An invalid request field.</summary>
    public record FieldError(string Field, string Message);

    public static class HttpHelpers
    {
        /// <summary>
        /// Builds an error response in RFC 9457 format (Content-Type: application/problem+json).
        /// </summary>
        public static IResult Problem(HttpContext ctx, int status, string title, string? detail = null,
                                      IEnumerable<FieldError>? errors = null, string? wwwAuthenticate = null)
        {
            if (wwwAuthenticate != null)
            {
                ctx.Response.Headers.WWWAuthenticate = wwwAuthenticate;
            }

            var extensions = new Dictionary<string, object?>();
            if (errors?.Any() == true)
            {
                extensions["errors"] = errors.Select(e => new { field = e.Field, message = e.Message });
            }

            return Results.Problem(type: "about:blank", title: title, statusCode: status,
                 detail: detail, instance: ctx.Request.Path, extensions: extensions);
        }

        /// <summary>
        /// Reads the request body as a JSON object. Returns either the object or a ready error
        /// response (415 for a wrong Content-Type, 400 for a body that is not a valid JSON object).
        /// </summary>
        public static async Task<JsonElement> ReadJsonObject(HttpContext ctx)
        {
            if (!ctx.Request.HasJsonContentType())
            {
                throw new ApiException(415, "Unsupported Media Type", "Content-Type must be application/json.");
            }

            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (body.ValueKind != JsonValueKind.Object)
                {
                    throw new ApiException(400, "Bad Request", "Request body must be a JSON object.");
                }
                return body;
            }
            catch (JsonException)
            {
                throw new ApiException(400, "Bad Request", "Request body is not valid JSON.");
            }
        }
    }
}