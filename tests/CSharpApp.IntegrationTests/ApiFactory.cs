using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;

namespace CSharpApp.IntegrationTests;

/// <summary>
/// The API in-process with its upstream replaced by WireMock on a random port: test credentials, fast retries, no
/// user-secrets and never the local mock on 5081, so tests run the same on any machine and in CI.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The fake upstream; stub it per test (see <see cref="UpstreamStubs"/>).</summary>
    public WireMockServer Upstream { get; } = WireMockServer.Start();

    /// <summary>The access token the upstream login stub hands out (ADR-0014); every resource call carries it.</summary>
    public string UpstreamToken { get; }

    /// <summary>The connection's remote IP; TestServer leaves it null, so rate-limit tests set it (R42).</summary>
    public IPAddress? RemoteIp { get; set; }

    /// <summary>The inbound API key header (ADR-0012).</summary>
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>The API key every client from this factory sends by default; tests without a key remove the header.</summary>
    public const string TestApiKey = "integration-test-api-key-0123456789"; // gitleaks:allow (test value, ADR-0012)

    public ApiFactory() => UpstreamToken = Upstream.StubLogin();

    /// <summary>Settings a test overrides on top of the defaults below, applied last.</summary>
    public IDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["RestApiSettings:BaseUrl"] = $"{Upstream.Url}/api/v1/",
                ["RestApiSettings:Username"] = "user@test",
                ["RestApiSettings:Password"] = "test-password",
                ["HttpClientSettings:RetryBaseDelayMs"] = "1",
                // Generous on purpose: a cold host under a parallel test run can take over a second for its first call,
                // and a POST is never retried. Timeout tests set their own short values.
                ["HttpClientSettings:AttemptTimeoutSeconds"] = "5",
                ["HttpClientSettings:TotalTimeoutSeconds"] = "10",
                // Shared factories send many requests from one (null) IP; rate-limit tests set their own limit.
                ["RateLimiting:PermitLimit"] = "100000",
                ["ApiAuth:Keys:0:ClientId"] = "integration-tests",
                ["ApiAuth:Keys:0:Key"] = TestApiKey,
            })
            .AddInMemoryCollection(Settings));
        if (RemoteIp is { } ip)
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(ip)));
        }
    }

    /// <summary>W22: POST tests written before API keys existed keep working: every client sends the test key.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, TestApiKey);
    }

    /// <summary>Sets the connection's remote IP ahead of the app's own pipeline (before forwarded headers).</summary>
    private sealed class RemoteIpStartupFilter(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate call) =>
            {
                context.Connection.RemoteIpAddress = ip;
                return call(context);
            });
            next(app);
        };
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Upstream.Dispose();
    }
}
