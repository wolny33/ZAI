using Microsoft.AspNetCore.Diagnostics;

namespace Backend.Common
{
    /// <summary>
    /// Turns exceptions into RFC 9457 error responses: ApiException keeps the status and details it carries,
    /// anything unexpected becomes a generic 500, details go to the log.
    /// </summary>
    public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken cancellationToken)
        {
            IResult response;
            if (exception is ApiException api)
            {
                response = HttpHelpers.Problem(ctx, api.Status, api.Title, api.Detail, api.Errors, api.WwwAuthenticate);
            }
            else if (exception is BadHttpRequestException bad)
            {
                response = HttpHelpers.Problem(ctx, bad.StatusCode, "Bad Request", "The request could not be processed.");
            }
            else
            {
                log.LogError(exception, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                response = HttpHelpers.Problem(ctx, 500, "Internal Server Error", "An unexpected error occurred.");
            }

            await response.ExecuteAsync(ctx);
            // true = handled, so no other handler or fallback is called
            return true;
        }
    }
}
