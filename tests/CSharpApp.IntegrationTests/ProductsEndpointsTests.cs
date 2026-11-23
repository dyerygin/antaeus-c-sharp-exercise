using System.Net;
using System.Net.Http.Json;
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
        var url = Assert.Single(_factory.Upstream.ResourceEntries).RequestMessage!.Url;
        Assert.EndsWith("/api/v1/products?offset=0&limit=20", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProducts_OffsetAndLimit_PassedThrough()
    {
        _factory.Upstream.StubJson("GET", "products", 200, Array.Empty<object>());
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products?offset=40&limit=100", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = Assert.Single(_factory.Upstream.ResourceEntries).RequestMessage!.Url;
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

    private static readonly object CreateBody = new
    {
        title = "Lamp",
        price = 10.5m,
        description = "A lamp",
        categoryId = 1,
        images = new[] { "https://images.test/lamp.png" },
    };

    [Fact]
    public async Task CreateProduct_Valid_Returns201WithLocation()
    {
        // ASM-05: the upstream echoes 10.5 but stores 10; the response shows what was stored (ADR-0011).
        _factory.Upstream.StubJson("POST", "products", 201, new ProductBuilder().WithId(42).WithPrice(10.5m).Build());
        _factory.Upstream.StubJson("GET", "products/42", 200, new ProductBuilder().WithId(42).WithPrice(10m).Build());
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/v1/products/42", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(10m, body.RootElement.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_Returns400OnCategoryId()
    {
        _factory.Upstream.StubEntityNotFound("POST", "products");
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("categoryId", out _));
    }

    [Fact]
    public async Task CreateProduct_Upstream500_NotRetried_Returns502()
    {
        _factory.Upstream.StubRaw("POST", "products", 500);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), CreateBody, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(_factory.Upstream.ResourceEntries);
    }
}
