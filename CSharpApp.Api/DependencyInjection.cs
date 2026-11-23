using System.Text.Json.Serialization;
using CSharpApp.Api.ErrorHandling;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace CSharpApp.Api;

/// <summary>Registers the API layer: error mapping (ADR-0005) and request hardening (ADR-0006).</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds ProblemDetails, the exception handler, the JSON contract and the Kestrel hardening.</summary>
        public IServiceCollection AddApi()
        {
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

            // R12: no "Server: Kestrel" banner.
            services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);
            return services;
        }
    }
}
