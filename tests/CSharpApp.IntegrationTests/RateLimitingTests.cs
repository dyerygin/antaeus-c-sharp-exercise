using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace CSharpApp.IntegrationTests;

/// <summary>Fixed-window rate limit on /api by remote IP; forwarded headers only from known proxies (ADR-0016, R42, Q4).</summary>
public sealed class RateLimitingTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new()
    {
        RemoteIp = IPAddress.Parse("10.0.0.1"),
        Settings = { ["RateLimiting:PermitLimit"] = "2", ["RateLimiting:WindowSeconds"] = "60" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("[]"));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<HttpResponseMessage> GetCategoriesAsync(HttpClient client, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/categories", UriKind.Relative));
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Api_OverLimit_Returns429()
    {
        using var client = _factory.CreateClient();

        (await GetCategoriesAsync(client)).Dispose();
        (await GetCategoriesAsync(client)).Dispose();
        using var third = await GetCategoriesAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        // R21: the rejection still passes through the security headers.
        Assert.Equal("nosniff", Assert.Single(third.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(2, _factory.Upstream.ResourceEntries.Count);
    }

    [Fact]
    public async Task Health_NotRateLimited()
    {
        using var client = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ForwardedFor_FromUnknownProxy_Ignored()
    {
        // Q4: with no known proxies, a client cannot pick a new partition by sending its own X-Forwarded-For.
        using var client = _factory.CreateClient();

        (await GetCategoriesAsync(client, "203.0.113.1")).Dispose();
        (await GetCategoriesAsync(client, "203.0.113.2")).Dispose();
        using var third = await GetCategoriesAsync(client, "203.0.113.3");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task ForwardedFor_FromKnownProxy_UsedForPartition()
    {
        _factory.Settings["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1";
        using var client = _factory.CreateClient();

        (await GetCategoriesAsync(client, "203.0.113.1")).Dispose();
        (await GetCategoriesAsync(client, "203.0.113.1")).Dispose();
        using var third = await GetCategoriesAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }
}
