namespace CSharpApp.Core.Exceptions;

/// <summary>The upstream has no such entity: 404, or 400 with <c>EntityNotFoundError</c> (ASM-10).</summary>
public sealed class UpstreamNotFoundException(string message) : UpstreamException(message);
