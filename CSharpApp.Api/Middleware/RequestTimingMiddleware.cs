using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace CSharpApp.Api.Middleware;

/// <summary>Logs one line per request with method, path, status and elapsed time (Feature #4, ADR-0015, T8).</summary>
/// <remarks>
/// First in the pipeline (R21), so the time includes error mapping, security headers and rate limiting. No query
/// string, headers, body or client IP. An exception that escapes every handler is logged as 500 and rethrown.
/// </remarks>
public sealed partial class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger, TimeProvider time)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = time.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        finally
        {
            var elapsed = Math.Round(time.GetElapsedTime(start).TotalMilliseconds, 1);
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;
            LogRequest(logger, method, path, status, elapsed);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms")]
    private static partial void LogRequest(ILogger logger, string method, string path, int statusCode, double elapsedMs);
}
