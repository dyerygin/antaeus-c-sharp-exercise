using System.Net;
using System.Text.Json;

namespace CSharpApp.IntegrationTests;

/// <summary>Upstream failures reach the caller as ProblemDetails with <c>traceId</c>, never as the upstream body (ADR-0005).</summary>
public sealed class ErrorMappingTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData(503, """{"message":"upstream internals"}""", HttpStatusCode.BadGateway)]
    [InlineData(200, "null", HttpStatusCode.BadGateway)]
    public async Task GetProducts_UpstreamFailure_ReturnsProblemWithTraceId(int upstreamStatus, string upstreamBody, HttpStatusCode expected)
    {
        _factory.Upstream.StubRaw("GET", "products", upstreamStatus, upstreamBody);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("upstream internals", text, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(text);
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Upstream_MalformedJson_Returns502()
    {
        _factory.Upstream.StubRaw("GET", "products", 200, "<html>not json</html>");
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not json", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upstream_ConnectionRefused_Returns502()
    {
        _factory.Upstream.Stop();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task GetProducts_UpstreamDelayBeyondTimeout_Returns504()
    {
        _factory.Settings["HttpClientSettings:TotalTimeoutSeconds"] = "1";
        _factory.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/api/v1/products").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200).WithBody("[]").WithDelay(TimeSpan.FromSeconds(3)));
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }
}
