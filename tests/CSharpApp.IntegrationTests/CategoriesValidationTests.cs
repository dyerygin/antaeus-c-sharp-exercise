using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.IntegrationTests;

/// <summary>Invalid category input is a 400 with the field in <c>errors</c> and never reaches the upstream (ADR-0008).</summary>
public sealed class CategoriesValidationTests : IAsyncLifetime
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
        // Exact names: the error keys are the camelCase JSON names of the wire contract (ADR-0008).
        Assert.True(errors.TryGetProperty(field, out _), errors.GetRawText());
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateCategory_NameEmpty_Returns400(string name)
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/categories", UriKind.Relative),
            new { name, image = "https://images.test/books.png" }, Ct);

        await AssertRejectedAsync(response, "name");
    }

    [Fact]
    public async Task CreateCategory_ImageNotUrl_Returns400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/categories", UriKind.Relative),
            new { name = "Books", image = "not a url" }, Ct);

        await AssertRejectedAsync(response, "image");
    }

    [Theory]
    [InlineData("/api/v1/categories?limit=101")]
    [InlineData("/api/v1/categories?limit=0")]
    public async Task GetCategories_LimitOver100_Returns400(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        await AssertRejectedAsync(response, "limit");
    }

    [Fact]
    public async Task GetCategory_IdZero_Returns400()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/categories/0", UriKind.Relative), Ct);

        await AssertRejectedAsync(response, "id");
    }
}
