using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace CSharpApp.IntegrationTests;

/// <summary>A1: the OpenAPI document is served only when <c>OpenApi:Enabled</c> is set, with concrete versioned paths (R13).</summary>
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
    public async Task OpenApi_NotEnabled_Returns404()
    {
        using var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
