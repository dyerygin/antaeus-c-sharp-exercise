using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CSharpApp.Api.Idempotency;

/// <summary>Registers the idempotency store and its settings (ADR-0013).</summary>
public static class IdempotencyRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds the validated <see cref="IdempotencySettings"/> and the in-memory store (one instance, ASM-08).</summary>
        public IServiceCollection AddIdempotency(IConfiguration configuration)
        {
            services.AddOptions<IdempotencySettings>()
                .Bind(configuration.GetSection(IdempotencySettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
            return services;
        }
    }
}
