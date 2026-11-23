using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WireMock.Server;

namespace CSharpApp.IntegrationTests;

/// <summary>
/// The API in-process with its upstream replaced by WireMock on a random port: test credentials, short timeouts, no
/// user-secrets and never the local mock on 5081, so tests run the same on any machine and in CI.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The fake upstream; stub it per test (see <see cref="UpstreamStubs"/>).</summary>
    public WireMockServer Upstream { get; } = WireMockServer.Start();

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
                ["HttpClientSettings:AttemptTimeoutSeconds"] = "1",
                ["HttpClientSettings:TotalTimeoutSeconds"] = "3",
            })
            .AddInMemoryCollection(Settings));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Upstream.Dispose();
    }
}
