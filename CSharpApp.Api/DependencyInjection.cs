using System.Text.Json.Serialization;
using CSharpApp.Api.ErrorHandling;
using CSharpApp.Api.Health;
using CSharpApp.Api.Idempotency;
using CSharpApp.Api.Security;
using CSharpApp.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace CSharpApp.Api;

/// <summary>Registers the API layer: mediator (ADR-0007), validation (ADR-0008), OpenAPI, error mapping (ADR-0005), request hardening (ADR-0006) and health checks (ADR-0016).</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds the mediator, validation, OpenAPI, ProblemDetails, the exception handler, the JSON contract and the Kestrel hardening.</summary>
        public IServiceCollection AddApi(IConfiguration configuration)
        {
            // The source generator lives in this project and discovers the handlers in CSharpApp.Application (L47).
            services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
            services.AddValidation();
            services.AddOpenApi(options => options.AddDocumentTransformer((document, context, _) =>
            {
                // R13: Asp.Versioning.Http without its API explorer leaves the route's {version} in the paths;
                // the document is per version, so its paths are concrete (/api/v1/products).
                var paths = new OpenApiPaths();
                foreach (var (path, item) in document.Paths)
                {
                    paths[path.Replace("v{version}", context.DocumentName, StringComparison.Ordinal)] = item;
                }

                document.Paths = paths;

                return Task.CompletedTask;
            })
            .AddDocumentTransformer(ApiKeyOpenApi.AddSecuritySchemeAsync)
            .AddOperationTransformer(ApiKeyOpenApi.RequireOnAuthorizedOperationsAsync)
            .AddOperationTransformer(IdempotencyOpenApi.AddKeyHeaderAsync));
            services.AddOptions<PagingSettings>()
                .Bind(configuration.GetSection(PagingSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // ADR-0016, ASM-12: liveness runs "self" only; readiness adds the cached, authenticated upstream probe.
            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthEndpoints.Live])
                .AddCheck<UpstreamHealthCheck>("upstream");

            services.AddProblemDetails();
            services.AddExceptionHandler<ApiExceptionHandler>();

            // T5: one JSON contract. Web defaults give camelCase; nulls are omitted and numbers are never strings.
            // X1: inbound bodies get the JsonSerializerOptions.Strict rules (duplicate or unknown members, null in a
            // non-nullable member → 400). The upstream clients keep their own lenient options (ADR-0006).
            services.ConfigureHttpJsonOptions(options =>
            {
                var json = options.SerializerOptions;
                json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                json.NumberHandling = JsonNumberHandling.Strict;
                json.AllowDuplicateProperties = false;
                json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                json.RespectNullableAnnotations = true;
                json.RespectRequiredConstructorParameters = true;
            });

            services.AddOptions<ApiLimitsSettings>()
                .Bind(configuration.GetSection(ApiLimitsSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddOptions<KestrelServerOptions>().Configure<IOptions<ApiLimitsSettings>>((kestrel, limits) =>
            {
                // R12: no "Server: Kestrel" banner. A3: oversized bodies get 413 (ADR-0006).
                kestrel.AddServerHeader = false;
                kestrel.Limits.MaxRequestBodySize = limits.Value.MaxRequestBodyBytes;
            });
            return services;
        }
    }
}
