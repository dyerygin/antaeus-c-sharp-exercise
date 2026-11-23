using CSharpApp.Application.Common;
using CSharpApp.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.Timeout;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace CSharpApp.Api.ErrorHandling;

/// <summary>Maps every exception to an RFC 7807 ProblemDetails with <c>traceId</c>; never echoes upstream bodies or exception messages (ADR-0005).</summary>
public sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The caller is gone: nobody reads a body, and it is not a server fault (nginx's 499 convention).
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogClientAborted(logger, httpContext.Request.Method);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var problem = Map(exception);
        Log(exception, problem.Status!.Value, problem.Title);
        httpContext.Response.StatusCode = problem.Status.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        RequestValidationException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary(StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        },
        BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "The request could not be read."),
        UpstreamNotFoundException => Problem(StatusCodes.Status404NotFound, "The resource was not found."),
        UpstreamBadRequestException => Problem(StatusCodes.Status400BadRequest, "The upstream service rejected the request."),
        UpstreamTimeoutException => Problem(StatusCodes.Status504GatewayTimeout, "The upstream service did not answer in time."),
        UpstreamException or BrokenCircuitException => Problem(StatusCodes.Status502BadGateway, "The upstream service is unavailable."),
        TimeoutRejectedException => Problem(StatusCodes.Status504GatewayTimeout, "The upstream service did not answer in time."),
        _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };

    private void Log(Exception exception, int status, string? title)
    {
        switch (exception)
        {
            case UpstreamBadRequestException badRequest:
                LogUpstreamRejected(logger, badRequest.UpstreamMessage);
                break;
            case RequestValidationException or BadHttpRequestException or UpstreamNotFoundException:
                LogClientError(logger, status, title);
                break;
            case UpstreamException or BrokenCircuitException or TimeoutRejectedException:
                LogUpstreamFailure(logger, exception, status);
                break;
            default:
                LogUnhandled(logger, exception);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The client closed the {Method} request before the response")]
    private static partial void LogClientAborted(ILogger logger, string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request answered with {Status}: {Title}")]
    private static partial void LogClientError(ILogger logger, int status, string? title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The upstream service rejected the request: {UpstreamMessage}")]
    private static partial void LogUpstreamRejected(ILogger logger, string? upstreamMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream failure answered with {Status}")]
    private static partial void LogUpstreamFailure(ILogger logger, Exception exception, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception answered with 500")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
