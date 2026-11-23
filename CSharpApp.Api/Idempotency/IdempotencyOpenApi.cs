using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CSharpApp.Api.Idempotency;

/// <summary>Documents the optional <c>Idempotency-Key</c> header on idempotent endpoints (ADR-0013).</summary>
public static class IdempotencyOpenApi
{
    /// <summary>Adds the header parameter (1–100 characters) to operations marked with <see cref="IdempotentEndpointMetadata"/>.</summary>
    public static Task AddKeyHeaderAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentEndpointMetadata>().FirstOrDefault() is { } metadata)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = metadata.HeaderName,
                In = ParameterLocation.Header,
                Required = false,
                Description = "Optional. Retrying with the same key and body replays the first 2xx response instead of creating again.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = IdempotencyHeaders.MaxKeyLength },
            });
        }

        return Task.CompletedTask;
    }
}
