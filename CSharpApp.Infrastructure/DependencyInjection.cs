using CSharpApp.Infrastructure.Auth;
using CSharpApp.Infrastructure.Clients;
using CSharpApp.Infrastructure.Diagnostics;
using CSharpApp.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace CSharpApp.Infrastructure;

/// <summary>Registers the infrastructure layer: settings validated on start, the typed upstream clients and their auth.</summary>
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

            services.AddMetrics();
            services.TryAddSingleton<UpstreamMetrics>();
            AddUpstreamPipeline(services.AddHttpClient<IProductsClient, ProductsClient>(ConfigureUpstream), "products");
            AddUpstreamPipeline(services.AddHttpClient<ICategoriesClient, CategoriesClient>(ConfigureUpstream), "categories");

            // ADR-0014: login and refresh get no bearer and no retry; AuthClient has its own timeout (R32).
            var auth = services.AddHttpClient<IAuthClient, AuthClient>(ConfigureUpstream);
            AddTiming(auth, "auth");
            UseUpstreamPrimaryHandler(auth);
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<AccessTokenProvider>();
            services.AddSingleton<IAccessTokenProvider>(provider => provider.GetRequiredService<AccessTokenProvider>());
            services.AddTransient<BearerTokenHandler>();

            // ADR-0016, R4: the readiness probe authenticates like every resource call; no retry, its own timeout.
            var health = services.AddHttpClient(UpstreamHealthCheck.ClientName, ConfigureUpstream);
            AddTiming(health, "health");
            UseUpstreamPrimaryHandler(health);
            health.AddHttpMessageHandler<BearerTokenHandler>();
            services.TryAddSingleton<UpstreamHealthCheck>();
            return services;
        }
    }

    /// <summary>The shared outbound pipeline of every upstream client (ADR-0004).</summary>
    private static void AddUpstreamPipeline(IHttpClientBuilder builder, string client)
    {
        // ADR-0015, R38: the first handler added is the outermost, so the time covers retries and the 401 resend.
        AddTiming(builder, client);
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

        // ADR-0014: added after resilience, so it runs inside it: every attempt carries the current token and a 401
        // resend stays within that attempt's timeout.
        builder.AddHttpMessageHandler<BearerTokenHandler>();
    }

    /// <summary>Feature #4: logs and counts each call of <paramref name="client"/> (ADR-0015).</summary>
    private static void AddTiming(IHttpClientBuilder builder, string client) =>
        builder.AddHttpMessageHandler(provider => new UpstreamTimingHandler(
            client,
            provider.GetRequiredService<ILogger<UpstreamTimingHandler>>(),
            provider.GetRequiredService<UpstreamMetrics>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<HttpClientSettings>>()));

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
