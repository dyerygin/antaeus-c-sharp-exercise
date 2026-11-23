using System.Net;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.HealthChecks;
using CSharpApp.UnitTests.Clients;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace CSharpApp.UnitTests.HealthChecks;

/// <summary>Readiness: an authenticated upstream probe, cached (ADR-0016, ASM-12).</summary>
public sealed class UpstreamHealthCheckTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly IHttpClientFactory _clients = Substitute.For<IHttpClientFactory>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UpstreamHealthCheck CreateCheck(StubHttpMessageHandler upstream)
    {
        _clients.CreateClient(UpstreamHealthCheck.ClientName)
            .Returns(_ => new HttpClient(upstream, disposeHandler: false) { BaseAddress = new Uri("http://upstream.test/api/v1/") });
        return new UpstreamHealthCheck(
            _clients,
            Options.Create(new HttpClientSettings { HealthCheckTimeoutSeconds = 3, HealthCheckCacheSeconds = 10 }),
            Options.Create(ProductsClientTests.Settings),
            _time);
    }

    private static StubHttpMessageHandler Answers(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("[]") }));

    [Fact]
    public async Task Upstream200_Healthy()
    {
        var upstream = Answers(HttpStatusCode.OK);

        var result = await CreateCheck(upstream).CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.EndsWith("/api/v1/categories?limit=1", Assert.Single(upstream.Requests).Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upstream503_Unhealthy()
    {
        var result = await CreateCheck(Answers(HttpStatusCode.ServiceUnavailable)).CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task UpstreamUnreachable_Unhealthy()
    {
        var check = CreateCheck(new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task UpstreamTimeout_Unhealthy()
    {
        var check = CreateCheck(new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var pending = check.CheckHealthAsync(new HealthCheckContext(), Ct);
        _time.Advance(TimeSpan.FromSeconds(3));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task WithinCacheWindow_CallsUpstreamOnce()
    {
        var upstream = Answers(HttpStatusCode.OK);
        var check = CreateCheck(upstream);

        await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        _time.Advance(TimeSpan.FromSeconds(9));
        await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        Assert.Single(upstream.Requests);

        _time.Advance(TimeSpan.FromSeconds(2));
        await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        Assert.Equal(2, upstream.Requests.Count);
    }
}
