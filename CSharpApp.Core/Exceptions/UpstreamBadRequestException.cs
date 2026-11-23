namespace CSharpApp.Core.Exceptions;

/// <summary>The upstream rejected the request (400 or 422 other than not-found).</summary>
/// <param name="upstreamMessage">The upstream's own <c>message</c>, kept for logging only; never returned to the caller (ADR-0005).</param>
public sealed class UpstreamBadRequestException(string? upstreamMessage)
    : UpstreamException("The upstream service rejected the request.")
{
    /// <summary>The upstream's <c>message</c> (a string, or an array joined with "; "), for logs only.</summary>
    public string? UpstreamMessage { get; } = upstreamMessage;
}
