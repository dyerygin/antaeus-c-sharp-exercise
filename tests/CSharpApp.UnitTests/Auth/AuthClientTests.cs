using System.Net;
using System.Text.Json;
using CSharpApp.Core.Exceptions;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Auth;
using CSharpApp.UnitTests.Clients;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Auth;

public sealed class AuthClientTests
{
    private const string Tokens = """{"access_token":"access-1","refresh_token":"refresh-1"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AuthClient CreateClient(StubHttpMessageHandler handler, int authTimeoutSeconds = 10) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(ProductsClientTests.Settings.BaseUrl) },
            Options.Create(ProductsClientTests.Settings),
            Options.Create(new HttpClientSettings { AuthTimeoutSeconds = authTimeoutSeconds }));

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Login_PostsEmailAndPassword_ReadsTokens(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Json(Tokens, status);

        var tokens = await CreateClient(handler).LoginAsync("user@test", "secret", Ct);

        Assert.Equal(new("access-1", "refresh-1"), tokens);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/auth/login", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("user@test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("secret", body.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public async Task Refresh_PostsRefreshToken_ReadsTokens()
    {
        var handler = StubHttpMessageHandler.Json(Tokens, HttpStatusCode.Created);

        var tokens = await CreateClient(handler).RefreshAsync("refresh-0", Ct);

        Assert.Equal("access-1", tokens.AccessToken);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/auth/refresh-token", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("refresh-0", body.RootElement.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Login401_ThrowsUnavailable()
    {
        var handler = StubHttpMessageHandler.Json("""{"message":"Unauthorized","statusCode":401}""", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => CreateClient(handler).LoginAsync("user@test", "wrong", Ct));
    }

    [Fact]
    public async Task Login_Hangs_ThrowsUpstreamTimeout()
    {
        // The auth call has its own timeout, so a hanging login cannot hold the token lock forever.
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<UpstreamTimeoutException>(() => CreateClient(handler, authTimeoutSeconds: 1).LoginAsync("user@test", "secret", guard.Token));
    }
}
