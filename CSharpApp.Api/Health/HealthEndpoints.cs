using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CSharpApp.Api.Health;

/// <summary>Liveness (<c>self</c>) and readiness (<c>self</c> + <c>upstream</c>) endpoints (ADR-0016, ASM-12).</summary>
public static class HealthEndpoints
{
    /// <summary>Tag of the checks that liveness runs.</summary>
    public const string Live = "live";

    extension(IEndpointRouteBuilder app)
    {
        /// <summary>Maps <c>/health/live</c> and <c>/health/ready</c>; public, not rate limited, never cached.</summary>
        public IEndpointRouteBuilder MapHealthEndpoints()
        {
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(Live),
                ResponseWriter = WriteAsync,
            }).DisableRateLimiting();
            app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = WriteAsync }).DisableRateLimiting();
            return app;
        }
    }

    /// <summary>Status and duration per check only: no descriptions, exceptions or upstream details (public endpoint).</summary>
    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            }),
        }, cancellationToken: context.RequestAborted);
    }
}
