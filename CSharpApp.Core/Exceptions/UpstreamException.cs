namespace CSharpApp.Core.Exceptions;

/// <summary>Base of every failure the upstream catalogue reports or causes; the API maps each kind to a ProblemDetails status.</summary>
/// <remarks>Messages never carry upstream bodies, tokens or credentials, because callers never see them anyway (ADR-0005).</remarks>
public abstract class UpstreamException(string message, Exception? innerException = null) : Exception(message, innerException);
