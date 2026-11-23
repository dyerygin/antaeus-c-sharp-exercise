using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace CSharpApp.IntegrationTests;

/// <summary>Liveness and authenticated readiness through the whole pipeline (ADR-0016, R4, R39, ASM-12).</summary>
public sealed class HealthTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new() { Settings = { ["HttpClientSettings:HealthCheckCacheSeconds"] = "0" } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task HealthLive_Returns200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_TokenRequiringUpstream_Returns200()
    {
        // R4/R39: the upstream answers 401 without a token, so only an authenticated probe is ready.
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingGet()
                .WithHeader("Authorization", $"Bearer {_factory.UpstreamToken}"))
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingGet())
            .AtPriority(2)
            .RespondWith(Response.Create().WithStatusCode(401));
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var checks = body.RootElement.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(["self", "upstream"], checks);
    }

    [Fact]
    public async Task HealthReady_WithinCacheWindow_ProbesUpstreamOnce()
    {
        // A10 through DI: the readiness check must be the one cached instance, not a new check per request (review C I1).
        await using var factory = new ApiFactory { Settings = { ["HttpClientSettings:HealthCheckCacheSeconds"] = "60" } };
        factory.Upstream.StubRaw("GET", "categories", 200, "[]");
        using var client = factory.CreateClient();

        using var first = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        using var second = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(factory.Upstream.LogEntries, entry => string.Equals(entry.RequestMessage?.Path, "/api/v1/categories", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthReady_UpstreamDown_Returns503()
    {
        _factory.Upstream.StubRaw("GET", "categories", 503);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"Unhealthy\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("503", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthLive_UpstreamDown_Returns200()
    {
        _factory.Upstream.StubRaw("GET", "categories", 503);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
