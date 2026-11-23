extern alias mock;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using MockProgram = mock::Program;

namespace CSharpApp.IntegrationTests;

/// <summary>
/// The API wired to the in-process contract mock (A8, ADR-0003): real login, bearer tokens required on resources,
/// the mock's validation and quirks. No socket is opened: the upstream host name does not resolve.
/// </summary>
public sealed class MockBackedApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The contract mock, requiring tokens on resources as in Docker.</summary>
    public WebApplicationFactory<MockProgram> Mock { get; } = new WebApplicationFactory<MockProgram>()
        .WithWebHostBuilder(builder => builder.UseSetting("MockAuth:RequireTokenOnResources", "true"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                // ".invalid" never resolves (RFC 2606): a call that bypassed the in-process handler would fail.
                ["RestApiSettings:BaseUrl"] = "http://upstream-mock.invalid/api/v1/",
                ["RestApiSettings:Username"] = "john@mail.com",
                ["RestApiSettings:Password"] = "changeme",
                ["ApiAuth:Keys:0:ClientId"] = "e2e",
                ["ApiAuth:Keys:0:Key"] = ApiFactory.TestApiKey,
                ["RateLimiting:PermitLimit"] = "100000",
            }));

        // R10: registered after the app's own client setup, so this primary handler wins over the per-client one.
        builder.ConfigureTestServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = Mock.Server.CreateHandler())));
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, ApiFactory.TestApiKey);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Mock.DisposeAsync();
    }
}
