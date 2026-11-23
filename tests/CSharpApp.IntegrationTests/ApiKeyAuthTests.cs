using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpApp.TestData.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace CSharpApp.IntegrationTests;

/// <summary>API key on write endpoints only (ADR-0012, ASM-07).</summary>
public sealed class ApiKeyAuthTests : IAsyncDisposable
{
    private static readonly object CreateBody = new
    {
        title = "Lamp",
        price = 10,
        description = "A desk lamp",
        categoryId = 1,
        images = new[] { "https://images.test/lamp.png" },
    };

    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private HttpClient ClientWithKey(string? key)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ApiFactory.ApiKeyHeader);
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(ApiFactory.ApiKeyHeader, key);
        }

        return client;
    }

    [Fact]
    public async Task CreateProduct_NoApiKey_Returns401_NoUpstreamCall()
    {
        using var client = ClientWithKey(null);

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ApiKey", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task CreateCategory_NoApiKey_Returns401_NoUpstreamCall()
    {
        using var client = ClientWithKey(null);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/categories", UriKind.Relative), new { name = "Lamps", image = "https://images.test/c.png" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task CreateProduct_WrongApiKey_Returns401()
    {
        using var client = ClientWithKey("wrong-key-wrong-key-wrong-key-0000");

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.Upstream.ResourceEntries);
    }

    [Fact]
    public async Task CreateProduct_ValidApiKey_Returns201()
    {
        _factory.Upstream.StubJson("POST", "products", 201, new ProductBuilder().WithId(42).Build());
        _factory.Upstream.StubJson("GET", "products/42", 200, new ProductBuilder().WithId(42).Build());
        using var client = ClientWithKey(ApiFactory.TestApiKey);

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetProducts_NoApiKey_Returns200()
    {
        _factory.Upstream.StubJson("GET", "products", 200, Array.Empty<object>());
        using var client = ClientWithKey(null);

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_NotWrittenToLogs()
    {
        var sink = new CapturingSink();
        await using var factory = _factory.WithWebHostBuilder(builder => builder
            .ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(7).Build());
        const string wrongKey = "wrong-key-that-must-not-be-logged-0000";
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ApiFactory.ApiKeyHeader);
        var body = new { name = "Lamps", image = "https://images.test/c.png" };

        foreach (var key in new[] { wrongKey, ApiFactory.TestApiKey })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/categories", UriKind.Relative))
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add(ApiFactory.ApiKeyHeader, key);
            using var response = await client.SendAsync(request, Ct);
        }

        Assert.NotEmpty(sink.Events);
        foreach (var logEvent in sink.Events)
        {
            using var writer = new StringWriter();
            new Serilog.Formatting.Json.JsonFormatter().Format(logEvent, writer);
            Assert.DoesNotContain(wrongKey, writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(ApiFactory.TestApiKey, writer.ToString(), StringComparison.Ordinal);
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IEnumerable<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
