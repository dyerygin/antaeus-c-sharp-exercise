using System.Net;
using System.Text.Json;
using CSharpApp.TestData.Builders;

namespace CSharpApp.IntegrationTests;

/// <summary>Products endpoints through the whole pipeline with a WireMock upstream (ADR-0007, ADR-0010).</summary>
public sealed class ProductsEndpointsTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task GetProducts_NoLimit_UsesDefault20()
    {
        _factory.Upstream.StubJson("GET", "products", 200, new[] { new ProductBuilder().Build() });
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = Assert.Single(_factory.Upstream.LogEntries).RequestMessage!.Url;
        Assert.EndsWith("/api/v1/products?offset=0&limit=20", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProducts_OffsetAndLimit_PassedThrough()
    {
        _factory.Upstream.StubJson("GET", "products", 200, Array.Empty<object>());
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products?offset=40&limit=100", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = Assert.Single(_factory.Upstream.LogEntries).RequestMessage!.Url;
        Assert.EndsWith("/api/v1/products?offset=40&limit=100", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProduct_ById_ReturnsProduct()
    {
        _factory.Upstream.StubJson("GET", "products/7", 200, new ProductBuilder().WithId(7).Build());
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products/7", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(7, body.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetProduct_UnknownId_Returns404Problem()
    {
        _factory.Upstream.StubEntityNotFound("GET", "products/999");
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products/999", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
