using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.IntegrationTests;

/// <summary>Invalid input is a 400 with the field in <c>errors</c> and never reaches the upstream (ADR-0008).</summary>
public sealed class ProductsValidationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task AssertRejectedAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.EnumerateObject().Any(e => string.Equals(e.Name, field, StringComparison.OrdinalIgnoreCase)), errors.GetRawText());
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [InlineData("/api/v1/products?limit=101", "limit")]
    [InlineData("/api/v1/products?limit=0", "limit")]
    [InlineData("/api/v1/products?offset=-1", "offset")]
    public async Task GetProducts_LimitOver100_Returns400(string path, string field)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        await AssertRejectedAsync(response, field);
    }

    [Fact]
    public async Task GetProduct_IdZero_Returns400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products/0", UriKind.Relative), Ct);

        await AssertRejectedAsync(response, "id");
    }

    private static Dictionary<string, object?> ValidBody() => new(StringComparer.Ordinal)
    {
        ["title"] = "Lamp",
        ["price"] = 10.5m,
        ["description"] = "A lamp",
        ["categoryId"] = 1,
        ["images"] = new[] { "https://images.test/lamp.png" },
    };

    private async Task<HttpResponseMessage> PostAsync(string field, object? value)
    {
        var body = ValidBody();
        body[field] = value;
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), body, Ct);
    }

    [Fact]
    public async Task CreateProduct_PriceZero_Returns400()
    {
        using var response = await PostAsync("price", 0);

        await AssertRejectedAsync(response, "price");
    }

    [Fact]
    public async Task CreateProduct_ImagesEmpty_Returns400()
    {
        using var response = await PostAsync("images", Array.Empty<string>());

        await AssertRejectedAsync(response, "images");
    }

    [Fact]
    public async Task CreateProduct_ImageNotUrl_Returns400()
    {
        using var response = await PostAsync("images", new List<string> { "not a url" });

        await AssertRejectedAsync(response, "images");
    }

    [Fact]
    public async Task CreateProduct_TitleTooLong_Returns400()
    {
        using var response = await PostAsync("title", new string('x', 201));

        await AssertRejectedAsync(response, "title");
    }

    [Fact]
    public async Task CreateProduct_CategoryIdZero_Returns400()
    {
        using var response = await PostAsync("categoryId", 0);

        await AssertRejectedAsync(response, "categoryId");
    }

    [Fact]
    public async Task CreateProduct_TooManyImages_Returns400()
    {
        using var response = await PostAsync("images", Enumerable.Range(1, 11).Select(i => $"https://images.test/{i}.png").ToArray());

        await AssertRejectedAsync(response, "images");
    }
}
