namespace CSharpApp.Core.Exceptions;

/// <summary>The upstream could not serve the request: unreachable, 5xx, a redirect, or it refused our credentials (401/403).</summary>
public sealed class UpstreamUnavailableException(string message, Exception? innerException = null) : UpstreamException(message, innerException);
