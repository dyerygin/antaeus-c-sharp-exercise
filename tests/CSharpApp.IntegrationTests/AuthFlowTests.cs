using System.Net;
using CSharpApp.TestData.Builders;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace CSharpApp.IntegrationTests;

/// <summary>Upstream JWT through the whole API pipeline: bearer, refresh after 401, one login under load (ADR-0014).</summary>
public sealed class AuthFlowTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private void StubProductsFor(string token)
    {
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet().WithHeader("Authorization", $"Bearer {token}"))
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/products").UsingGet())
            .AtPriority(2)
            .RespondWith(Response.Create().WithStatusCode(401));
    }

    [Fact]
    public async Task UpstreamCalls_CarryBearerToken()
    {
        StubProductsFor(_factory.UpstreamToken);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_factory.Upstream.ResourceEntries).RequestMessage!.Headers!["Authorization"];
        Assert.Equal($"Bearer {_factory.UpstreamToken}", Assert.Single(sent));
    }

    [Fact]
    public async Task Upstream401_RefreshesAndSucceeds()
    {
        // The upstream revoked the cached token: one 401, one refresh, one resend with the new token (ADR-0014).
        var renewed = TestJwt.ExpiringAt(DateTimeOffset.UtcNow.AddDays(20), "renewed");
        _factory.Upstream.StubJson("POST", "auth/refresh-token", 201, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["access_token"] = renewed,
            ["refresh_token"] = "refresh-renewed",
        });
        StubProductsFor(renewed);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _factory.Upstream.ResourceEntries.Count);
        Assert.Single(_factory.Upstream.LoginEntries);
        Assert.Single(_factory.Upstream.LogEntries, e => e.RequestMessage!.Path.EndsWith("/auth/refresh-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentRequests_SingleLogin()
    {
        // End to end: the login is slow, so 20 first requests overlap while it runs.
        _factory.Upstream.ResetMappings();
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/auth/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"access_token":"{{_factory.UpstreamToken}}","refresh_token":"r"}""")
                .WithDelay(TimeSpan.FromMilliseconds(300)));
        StubProductsFor(_factory.UpstreamToken);
        using var client = _factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(_factory.Upstream.LoginEntries);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
