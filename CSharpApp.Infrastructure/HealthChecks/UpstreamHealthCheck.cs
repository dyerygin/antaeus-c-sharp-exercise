using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CSharpApp.Infrastructure.HealthChecks;

/// <summary>Readiness of the upstream: an authenticated <c>GET categories?limit=1</c>, cached (ADR-0016, ASM-12).</summary>
/// <remarks>
/// The named client carries the bearer handler (R4), so readiness also proves the login works and holds against an
/// upstream that requires a token on every resource. No retry; its own timeout. A singleton, so the result is reused for
/// <c>HealthCheckCacheSeconds</c> (A10) and a busy orchestrator cannot multiply upstream calls. Failure details stay
/// out of the result: the health endpoint is public.
/// </remarks>
public sealed class UpstreamHealthCheck(
    IHttpClientFactory clients,
    IOptions<HttpClientSettings> settings,
    IOptions<RestApiSettings> api,
    TimeProvider time) : IHealthCheck
{
    /// <summary>The named HTTP client of the probe.</summary>
    public const string ClientName = "upstream-health";

    private CachedResult? _cached;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _cached) is { } cached && time.GetUtcNow() < cached.ExpiresAt)
        {
            return cached.Result;
        }

        var result = await ProbeAsync(cancellationToken);
        Volatile.Write(ref _cached, new CachedResult(result, time.GetUtcNow().AddSeconds(settings.Value.HealthCheckCacheSeconds)));
        return result;
    }

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.Value.HealthCheckTimeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.GetAsync(new Uri($"{api.Value.Categories}?limit=1", UriKind.Relative), linked.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("The upstream answered.")
                : HealthCheckResult.Unhealthy($"The upstream answered {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The upstream did not answer in time.");
        }
        catch (Exception ex) when (ex is HttpRequestException or UpstreamException)
        {
            // Unreachable, or the login failed (bearer handler): not ready. The exception is not attached (public endpoint).
            return HealthCheckResult.Unhealthy("The upstream could not be reached.");
        }
    }

    private sealed record CachedResult(HealthCheckResult Result, DateTimeOffset ExpiresAt);
}
