namespace CSharpApp.Core.Exceptions;

/// <summary>The upstream did not finish answering (headers and body) within the total timeout; the API answers 504.</summary>
public sealed class UpstreamTimeoutException(string message, Exception? innerException = null) : UpstreamException(message, innerException);
