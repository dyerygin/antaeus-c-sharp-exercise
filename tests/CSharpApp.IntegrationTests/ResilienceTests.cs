using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace CSharpApp.IntegrationTests;

/// <summary>Resilience through the whole API pipeline: retries for GET only, no redirects (ADR-0004, R40).</summary>
public sealed class ResilienceTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task GetProducts_Upstream500Always_Returns502()
    {
        _factory.Upstream.StubRaw("GET", "products", 500);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        // RetryCount 2 (appsettings): the first attempt and two retries, then the error.
        Assert.Equal(3, _factory.Upstream.ResourceEntries.Count);
    }

    [Fact]
    public async Task Upstream302_NotFollowed_Returns502()
    {
        // R40: a redirect could send the bearer token to another host; it is never followed.
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(302).WithHeader("Location", $"{_factory.Upstream.Url}/elsewhere"));
        _factory.Upstream.Given(Request.Create().WithPath("/elsewhere").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(_factory.Upstream.LogEntries, e => string.Equals(e.RequestMessage!.Path, "/elsewhere", StringComparison.Ordinal));
    }
}
