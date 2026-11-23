namespace CSharpApp.Core.Exceptions;

/// <summary>A successful upstream response whose body is empty, <c>null</c> or not the agreed JSON (F-08).</summary>
public sealed class UpstreamContractException(string message, Exception? innerException = null) : UpstreamException(message, innerException);
