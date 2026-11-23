namespace CSharpApp.Api.Middleware;

/// <summary>Adds browser-hardening headers to every response, and <c>Cache-Control: no-store</c> to errors (ADR-0006).</summary>
/// <remarks>
/// Headers are set in <c>OnStarting</c>, not before <c>next</c>: the exception handler clears the response before it
/// writes a ProblemDetails, which would drop headers set earlier.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = (HttpResponse)state;
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.XFrameOptions = "DENY";
            response.Headers["Referrer-Policy"] = "no-referrer";
            if (response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                // Error bodies carry a traceId and request-specific detail; no proxy or browser should keep them.
                response.Headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        }, context.Response);

        return next(context);
    }
}
