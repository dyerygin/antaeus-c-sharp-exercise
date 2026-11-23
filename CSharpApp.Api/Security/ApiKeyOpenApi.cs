using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CSharpApp.Api.Security;

/// <summary>Declares the <c>ApiKey</c> scheme in the OpenAPI document and on the operations that require it (ADR-0012).</summary>
public static class ApiKeyOpenApi
{
    /// <summary>Adds the <c>ApiKey</c> security scheme (header <c>X-Api-Key</c>) to the document's components.</summary>
    public static Task AddSecuritySchemeAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[ApiKeyAuthenticationOptions.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationOptions.HeaderName,
            Description = "API key of the calling client; required on create endpoints only.",
        };
        return Task.CompletedTask;
    }

    /// <summary>Marks every operation with authorization metadata (the POST endpoints) as requiring the key.</summary>
    public static Task RequireOnAuthorizedOperationsAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationOptions.Scheme, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
