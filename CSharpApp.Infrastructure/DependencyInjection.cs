using CSharpApp.Infrastructure.Auth;
using CSharpApp.Infrastructure.Clients;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace CSharpApp.Infrastructure;

/// <summary>Registers the infrastructure layer: settings validated on start and the typed upstream clients.</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Binds and validates the upstream settings (no <c>BuildServiceProvider</c>, F-11) and adds the typed clients.</summary>
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            services.AddOptions<RestApiSettings>()
                .Bind(configuration.GetSection(RestApiSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddOptions<HttpClientSettings>()
                .Bind(configuration.GetSection(HttpClientSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            AddUpstreamPipeline(services.AddHttpClient<IProductsClient, ProductsClient>(ConfigureUpstream));
            AddUpstreamPipeline(services.AddHttpClient<ICategoriesClient, CategoriesClient>(ConfigureUpstream));

            // ADR-0014: login and refresh get no bearer and no retry; AuthClient has its own timeout (R32).
            UseUpstreamPrimaryHandler(services.AddHttpClient<IAuthClient, AuthClient>(ConfigureUpstream));
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<AccessTokenProvider>();
            services.AddSingleton<IAccessTokenProvider>(provider => provider.GetRequiredService<AccessTokenProvider>());
            return services;
        }
    }

    /// <summary>The shared outbound pipeline of every upstream client (ADR-0004).</summary>
    private static void AddUpstreamPipeline(IHttpClientBuilder builder)
    {
        UseUpstreamPrimaryHandler(builder);

        builder.AddStandardResilienceHandler().Configure((options, provider) =>
        {
            var settings = provider.GetRequiredService<IOptions<HttpClientSettings>>().Value;
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(settings.TotalTimeoutSeconds);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds);
            options.Retry.MaxRetryAttempts = settings.RetryCount;
            options.Retry.Delay = TimeSpan.FromMilliseconds(settings.RetryBaseDelayMs);
            // ADR-0004: POST is not idempotent upstream; a retry could create the product twice.
            options.Retry.DisableForUnsafeHttpMethods();
            // The circuit breaker requires a sampling window of at least twice the attempt timeout.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, 2 * settings.AttemptTimeoutSeconds));
        });

    }

    /// <summary>A4: never follow redirects to another host; recycle pooled connections so DNS changes are seen.</summary>
    private static void UseUpstreamPrimaryHandler(IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

    private static void ConfigureUpstream(IServiceProvider provider, HttpClient client) =>
        client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<RestApiSettings>>().Value.BaseUrl);
}
