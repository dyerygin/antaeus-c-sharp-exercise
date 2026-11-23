using System.Text.Json;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Exceptions;
using CSharpApp.Core.Interfaces;
using CSharpApp.Infrastructure;
using CSharpApp.TestData.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpApp.IntegrationTests.Upstream;

/// <summary>The registered client pipeline (ADR-0004) against a real HTTP server on a random port.</summary>
public sealed class ResiliencePipelineTests : IDisposable
{
    private readonly WireMockServer _upstream = WireMockServer.Start();
    private readonly ServiceProvider _services;

    public ResiliencePipelineTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RestApiSettings:BaseUrl"] = $"{_upstream.Url}/api/v1/",
            ["RestApiSettings:Products"] = "products",
            ["RestApiSettings:Categories"] = "categories",
            ["RestApiSettings:Auth"] = "auth/login",
            ["RestApiSettings:RefreshToken"] = "auth/refresh-token",
            ["RestApiSettings:Profile"] = "auth/profile",
            ["RestApiSettings:Username"] = "user@test",
            ["RestApiSettings:Password"] = "test-password",
            ["HttpClientSettings:RetryBaseDelayMs"] = "1",
            ["HttpClientSettings:AttemptTimeoutSeconds"] = "2",
            ["HttpClientSettings:TotalTimeoutSeconds"] = "5",
        }).Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _services.Dispose();
        _upstream.Dispose();
    }

    [Fact]
    public async Task Create_Upstream500_NotRetried()
    {
        _upstream.Given(Request.Create().WithPath("/api/v1/products").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        var client = _services.GetRequiredService<IProductsClient>();

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() =>
            client.CreateAsync(new NewProduct("Lamp", 10m, "A lamp", 1, ["https://images.test/lamp.png"]), Ct));

        Assert.Single(_upstream.LogEntries);
    }

    [Fact]
    public async Task GetAll_Upstream500ThenOk_RetriedAndSucceeds()
    {
        _upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet())
            .InScenario("flaky").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(500));
        _upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet())
            .InScenario("flaky").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new[] { new ProductBuilder().Build() }, JsonSerializerOptions.Web)));
        var client = _services.GetRequiredService<IProductsClient>();

        var products = await client.GetAllAsync(0, 20, Ct);

        Assert.Single(products);
        Assert.Equal(2, _upstream.LogEntries.Count);
    }

    [Fact]
    public async Task RepeatedCalls_Succeed()
    {
        // F-01 regression: the base code set BaseAddress on a shared HttpClient per call, so the second call failed.
        _upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
        var client = _services.GetRequiredService<IProductsClient>();

        await client.GetAllAsync(0, 20, Ct);
        await _services.GetRequiredService<IProductsClient>().GetAllAsync(0, 20, Ct);

        Assert.Equal(2, _upstream.LogEntries.Count);
    }

    [Theory]
    [InlineData(nameof(IProductsClient))]
    [InlineData(nameof(ICategoriesClient))]
    public void ClientTimeout_IsInfinite(string clientName)
    {
        // Review A M-1: HttpClient.Timeout (100 s by default) must not cut a call before the configured total timeout;
        // the standard resilience handler sets it to infinite, so only the configured timeouts apply.
        using var client = _services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void AccessTokenProvider_IsOneSingleton()
    {
        // §6 item 1 depends on every caller sharing one cache and one login gate.
        Assert.Same(_services.GetRequiredService<IAccessTokenProvider>(), _services.GetRequiredService<IAccessTokenProvider>());
    }

    [Theory]
    [InlineData(nameof(IProductsClient))]
    [InlineData(nameof(ICategoriesClient))]
    [InlineData("IAuthClient")]
    public void PrimaryHandler_IsSocketsHttpHandler_WithoutAutoRedirect(string clientName)
    {
        var factory = _services.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler handler = factory.CreateHandler(clientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
        Assert.Equal(TimeSpan.FromMinutes(2), primary.PooledConnectionLifetime);
    }
}
