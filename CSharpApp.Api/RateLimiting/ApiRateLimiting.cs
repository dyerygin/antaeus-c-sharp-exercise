using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.RateLimiting;

/// <summary>Rate limiting of the <c>/api</c> endpoints and the forwarded-header trust it depends on (ADR-0016).</summary>
public static class ApiRateLimiting
{
    /// <summary>The policy the <c>/api</c> groups require.</summary>
    public const string Policy = "api";

    extension(IServiceCollection services)
    {
        /// <summary>Adds the fixed-window limiter (429 ProblemDetails with <c>Retry-After</c>) and the forwarded headers.</summary>
        public IServiceCollection AddApiRateLimiting(IConfiguration configuration)
        {
            services.AddOptions<RateLimitingSettings>()
                .Bind(configuration.GetSection(RateLimitingSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<ForwardedHeadersOptions>().Configure(options =>
            {
                // Trust X-Forwarded-For only from configured proxies, so a client cannot pick its own rate-limit
                // partition. The loopback defaults are dropped too. With both lists empty the middleware would trust
                // every sender, so no configured proxy means no forwarded headers at all.
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                {
                    options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address)
                        ? address
                        : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains '{proxy}', which is not an IP address."));
                }

                options.ForwardedHeaders = options.KnownProxies.Count == 0
                    ? ForwardedHeaders.None
                    : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = WriteRejectionAsync;
                options.AddPolicy(Policy, context =>
                {
                    var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                    return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0,
                    });
                });
            });
            return services;
        }
    }

    /// <summary><c>client_id</c> of an API-key caller (ADR-0012), else the remote IP, else one shared partition.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst("client_id")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken ct)
    {
        var context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
        });
    }
}
