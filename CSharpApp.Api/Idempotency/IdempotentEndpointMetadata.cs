namespace CSharpApp.Api.Idempotency;

/// <summary>Marks an endpoint that accepts <c>Idempotency-Key</c>, so OpenAPI documents the header.</summary>
/// <param name="HeaderName">The header the endpoint reads.</param>
public sealed record IdempotentEndpointMetadata(string HeaderName = IdempotencyHeaders.Key);
