using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpApp.TestData.Builders;

namespace CSharpApp.IntegrationTests;

/// <summary>Categories endpoints through the whole pipeline with a WireMock upstream (ADR-0010, ASM-11).</summary>
public sealed class CategoriesEndpointsTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task GetCategories_NoLimit_UsesDefault20()
    {
        _factory.Upstream.StubJson("GET", "categories", 200, new[] { new CategoryBuilder().Build() });
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("/api/v1/categories?limit=20", Assert.Single(_factory.Upstream.LogEntries).RequestMessage!.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCategory_ById_ReturnsCategory()
    {
        _factory.Upstream.StubJson("GET", "categories/4", 200, new CategoryBuilder().WithId(4).Build());
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/categories/4", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(4, body.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetCategory_UnknownId_Returns404Problem()
    {
        _factory.Upstream.StubEntityNotFound("GET", "categories/999");
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/categories/999", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateCategory_Valid_Returns201()
    {
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(6).WithName("Books").Build());
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/categories", UriKind.Relative),
            new { name = "Books", image = "https://images.test/books.png" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/v1/categories/6", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Single(_factory.Upstream.LogEntries);
    }
}
