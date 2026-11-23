using System.Net;
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
}
