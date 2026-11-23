namespace CSharpApp.Api.Idempotency;

/// <summary>A completed 2xx response, kept so a retry with the same key gets the same answer (ADR-0013).</summary>
/// <param name="StatusCode">The status sent the first time.</param>
/// <param name="Location">The <c>Location</c> header, if any.</param>
/// <param name="Body">The response body bytes.</param>
/// <param name="ContentType">The body's content type.</param>
public sealed record StoredResponse(int StatusCode, string? Location, ReadOnlyMemory<byte> Body, string ContentType);
