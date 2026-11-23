using System.Net;
using System.Net.Http.Json;
using CSharpApp.Core.Interfaces;
using CSharpApp.Infrastructure.Auth;
using CSharpApp.UnitTests.TestDoubles;
using NSubstitute;

namespace CSharpApp.UnitTests.Auth;

/// <summary>Bearer token on every upstream call and one resend after a 401 (ADR-0014, R15, §6 item 5).</summary>
public sealed class BearerTokenHandlerTests
{
    private static readonly Uri ProductsUri = new("http://upstream.test/api/v1/products");

    private readonly IAccessTokenProvider _tokens = Substitute.For<IAccessTokenProvider>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient CreateClient(StubHttpMessageHandler upstream) =>
        new(new BearerTokenHandler(_tokens) { InnerHandler = upstream });

    private static StubHttpMessageHandler Answers(params HttpStatusCode[] statuses)
    {
        var next = 0;
        return new StubHttpMessageHandler((_, _) =>
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref next) - 1, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("[]") });
        });
    }

    [Fact]
    public async Task Send_AddsBearerHeader()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-a");
        var upstream = Answers(HttpStatusCode.OK);
        using var client = CreateClient(upstream);

        using var response = await client.GetAsync(ProductsUri, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer token-a", Assert.Single(upstream.Requests).Authorization);
        _tokens.DidNotReceive().Invalidate(Arg.Any<string>());
    }

    [Fact]
    public async Task Send_401ThenOk_RetriesOnceWithNewToken()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-a", "token-b");
        var upstream = Answers(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var client = CreateClient(upstream);

        using var response = await client.GetAsync(ProductsUri, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer token-a", "Bearer token-b"], upstream.Requests.Select(r => r.Authorization), StringComparer.Ordinal);
        _tokens.Received(1).Invalidate("token-a");
    }

    [Fact]
    public async Task Send_401Twice_RetriesOnce()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-a", "token-b", "token-c");
        var upstream = Answers(HttpStatusCode.Unauthorized);
        using var client = CreateClient(upstream);

        using var response = await client.GetAsync(ProductsUri, Ct);

        // The second 401 goes back to the client, which maps it to 502 (ADR-0005); no loop.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, upstream.Requests.Count);
        _tokens.Received(1).Invalidate("token-a");
        _tokens.DidNotReceive().Invalidate("token-b");
    }

    [Fact]
    public async Task Send_Post401_ResendsSameBody()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-a", "token-b");
        var upstream = Answers(HttpStatusCode.Unauthorized, HttpStatusCode.Created);
        using var client = CreateClient(upstream);

        using var response = await client.PostAsJsonAsync(ProductsUri, new { title = "Lamp", price = 10 }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, upstream.Requests.Count);
        Assert.All(upstream.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.Equal("""{"title":"Lamp","price":10}""", upstream.Requests[0].Body);
        Assert.Equal(upstream.Requests[0].Body, upstream.Requests[1].Body);
        Assert.Equal("Bearer token-b", upstream.Requests[1].Authorization);
    }

    [Fact]
    public async Task Send_403_NotRetried()
    {
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("token-a");
        var upstream = Answers(HttpStatusCode.Forbidden);
        using var client = CreateClient(upstream);

        using var response = await client.GetAsync(ProductsUri, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(upstream.Requests);
        _tokens.DidNotReceive().Invalidate(Arg.Any<string>());
    }
}
