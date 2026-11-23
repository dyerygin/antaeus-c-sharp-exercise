using System.Net;
using System.Text.Json;

namespace CSharpApp.IntegrationTests;

/// <summary>Security headers, no server banner and one JSON contract on every response (ADR-0006, A5, T5).</summary>
public sealed class HardeningTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData("/openapi/v1.json", HttpStatusCode.OK)]
    [InlineData("/no-such-route", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/products", HttpStatusCode.BadGateway)]
    public async Task AnyResponse_HasSecurityHeaders(string path, HttpStatusCode expected)
    {
        _factory.Upstream.StubRaw("GET", "products", 503);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/no-such-route")]
    [InlineData("/api/v1/products")]
    public async Task ErrorResponse_IsNotCached(string path)
    {
        _factory.Upstream.StubRaw("GET", "products", 503);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.True(response.Headers.CacheControl?.NoStore, $"Cache-Control: {response.Headers.CacheControl}");
    }

    [Fact]
    public async Task Response_HasNoServerHeader()
    {
        // TestServer never sends Server; only a real Kestrel listener shows whether the banner is off (R12).
        _factory.UseKestrel(0);
        _factory.StartServer();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Server"), $"Server: {response.Headers.Server}");
    }

    [Fact]
    public async Task Response_UsesCamelCase_OmitsNulls()
    {
        _factory.Upstream.StubRaw("GET", "products", 200, """[{"id":1,"title":"Lamp","price":10.5,"description":null,"images":[]}]""");
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        var product = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement[0];
        Assert.Equal("Lamp", product.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Number, product.GetProperty("price").ValueKind);
        Assert.Equal(10.5m, product.GetProperty("price").GetDecimal());
        Assert.False(product.TryGetProperty("description", out _));
        Assert.False(product.TryGetProperty("category", out _));
    }
}
