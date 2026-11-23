using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.IntegrationTests;

/// <summary>The whole chain against the contract mock: API key, upstream JWT, mock validation and quirks (ADR-0003).</summary>
public sealed class EndToEndTests : IAsyncDisposable
{
    private readonly MockBackedApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static object NewProduct(string title) => new
    {
        title,
        price = 10.5,
        description = "End-to-end product",
        categoryId = 1,
        images = new[] { "https://images.test/e2e.png" },
    };

    [Fact]
    public async Task E2E_Login_CreateProduct_GetIt()
    {
        using var client = _factory.CreateClient();

        using var created = await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), NewProduct("E2E Lamp"), Ct);
        using var fetched = await client.GetAsync(created.Headers.Location, Ct);

        // ASM-05: the mock stores 10.5 as 10, and the read-back returns what was stored (ADR-0011).
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(10m, (await ReadAsync(created)).GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var product = await ReadAsync(fetched);
        Assert.Equal("E2E Lamp", product.GetProperty("title").GetString());
        Assert.Equal(10m, product.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task E2E_CreateCategory_ListContainsIt()
    {
        using var client = _factory.CreateClient();

        using var created = await client.PostAsJsonAsync(
            new Uri("/api/v1/categories", UriKind.Relative), new { name = "E2E Category", image = "https://images.test/c.png" }, Ct);
        using var list = await client.GetAsync(new Uri("/api/v1/categories?limit=100", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains((await ReadAsync(list)).EnumerateArray(), c => string.Equals(c.GetProperty("name").GetString(), "E2E Category", StringComparison.Ordinal));
    }

    [Fact]
    public async Task E2E_UnknownProduct_Returns404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products/999999", UriKind.Relative), Ct);

        // ASM-10: the mock answers 400 EntityNotFoundError, which the API maps to 404 (ADR-0005).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task E2E_IdempotentCreate_SingleProductInStore()
    {
        using var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/products", UriKind.Relative))
            {
                Content = JsonContent.Create(NewProduct("E2E Idempotent")),
            };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Assert.Equal(1, await CountInMockAsync("E2E Idempotent"));
    }

    /// <summary>Asks the mock itself (with its own login), so the count is what the upstream stored.</summary>
    private async Task<int> CountInMockAsync(string title)
    {
        using var mock = _factory.Mock.CreateClient();
        using var login = await mock.PostAsJsonAsync("api/v1/auth/login", new { email = "john@mail.com", password = "changeme" }, Ct);
        var token = (await ReadAsync(login)).GetProperty("access_token").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/v1/products?limit=1000", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var products = await mock.SendAsync(request, Ct);
        return (await ReadAsync(products)).EnumerateArray().Count(p => string.Equals(p.GetProperty("title").GetString(), title, StringComparison.Ordinal));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
}
