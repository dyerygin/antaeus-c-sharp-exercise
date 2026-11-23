using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace CSharpApp.IntegrationTests;

/// <summary>The OpenAPI document is served only when <c>OpenApi:Enabled</c> is set, with concrete versioned paths.</summary>
public sealed class OpenApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/openapi/v1.yaml")]
    public async Task OpenApi_EnabledByConfig_Returns200(string path)
    {
        _factory.Settings["OpenApi:Enabled"] = "true";
        using var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/api/v1/products", document, StringComparison.Ordinal);
        Assert.DoesNotContain("{version}", document, StringComparison.Ordinal);
        Assert.Contains("The upstream service did not answer in time.", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_CreateOperations_DocumentApiKeyAndIdempotency_GetsDoNot()
    {
        _factory.Settings["OpenApi:Enabled"] = "true";
        using var client = _factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct));

        var root = document.RootElement;
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());
        foreach (var path in new[] { "/api/v1/products", "/api/v1/categories" })
        {
            var item = root.GetProperty("paths").GetProperty(path);
            Assert.True(item.GetProperty("post").TryGetProperty("security", out _), $"POST {path} declares no security");
            Assert.True(item.GetProperty("post").GetProperty("responses").TryGetProperty("401", out _), $"POST {path} has no 401");
            Assert.False(item.GetProperty("get").TryGetProperty("security", out _), $"GET {path} declares security");
            var post = item.GetProperty("post");
            Assert.True(post.GetProperty("responses").TryGetProperty("409", out _), $"POST {path} has no 409");
            Assert.True(post.GetProperty("responses").TryGetProperty("422", out _), $"POST {path} has no 422");
            var header = Assert.Single(post.GetProperty("parameters").EnumerateArray(),
                p => string.Equals(p.GetProperty("name").GetString(), "Idempotency-Key", StringComparison.Ordinal));
            Assert.Equal("header", header.GetProperty("in").GetString());
            Assert.Equal(100, header.GetProperty("schema").GetProperty("maxLength").GetInt32());
        }
    }

    [Fact]
    public async Task OpenApi_NotEnabled_Returns404()
    {
        using var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
