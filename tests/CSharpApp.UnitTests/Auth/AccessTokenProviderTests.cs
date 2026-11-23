using CSharpApp.Core.Dtos;
using CSharpApp.Core.Exceptions;
using CSharpApp.Infrastructure.Auth;
using CSharpApp.TestData.Builders;
using CSharpApp.UnitTests.Clients;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CSharpApp.UnitTests.Auth;

/// <summary>Token cache, refresh-then-login and single-flight login (ADR-0014, R31, R32, R41, §6 item 1).</summary>
public sealed class AccessTokenProviderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    private readonly IAuthClient _auth = Substitute.For<IAuthClient>();
    private readonly ListLogger<AccessTokenProvider> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AccessTokenProvider CreateProvider() => new(_auth, Options.Create(ProductsClientTests.Settings), _time, _logger);

    private AuthTokens Tokens(string id, TimeSpan lifetime) => new(TestJwt.ExpiringAt(_time.GetUtcNow() + lifetime, id), $"refresh-{id}");

    [Fact]
    public async Task FirstCall_LogsIn()
    {
        var tokens = Tokens("a", TimeSpan.FromDays(20));
        _auth.LoginAsync("user@test", "test-password", Arg.Any<CancellationToken>()).Returns(tokens);

        var token = await CreateProvider().GetTokenAsync(Ct);

        Assert.Equal(tokens.AccessToken, token);
        await _auth.Received(1).LoginAsync("user@test", "test-password", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondCallBeforeExpiry_UsesCache()
    {
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Tokens("a", TimeSpan.FromDays(20)));
        var provider = CreateProvider();

        var first = await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromDays(19));
        var second = await provider.GetTokenAsync(Ct);

        Assert.Equal(first, second);
        await _auth.Received(1).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterExpiryMinusSkew_Refreshes()
    {
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Tokens("a", TimeSpan.FromHours(1)));
        var provider = CreateProvider();
        await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(59));
        var refreshed = Tokens("b", TimeSpan.FromHours(1));
        _auth.RefreshAsync("refresh-a", Arg.Any<CancellationToken>()).Returns(refreshed);

        var token = await provider.GetTokenAsync(Ct);

        Assert.Equal(refreshed.AccessToken, token);
        await _auth.Received(1).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshFails_FallsBackToLogin()
    {
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Tokens("a", TimeSpan.FromHours(1)), Tokens("c", TimeSpan.FromHours(1)));
        _auth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new UpstreamUnavailableException("refresh token expired"));
        var provider = CreateProvider();
        await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromHours(2));

        var token = await provider.GetTokenAsync(Ct);

        Assert.Contains(".", token, StringComparison.Ordinal);
        await _auth.Received(2).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _auth.Received(1).RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCallers_LoginOnce()
    {
        // R31: the login is held open until every caller is waiting, so the 20 calls truly overlap.
        var release = new TaskCompletionSource<AuthTokens>(TaskCreationOptions.RunContinuationsAsynchronously);
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(release.Task);
        var provider = CreateProvider();

        var callers = Enumerable.Range(0, 20).Select(_ => Task.Run(() => provider.GetTokenAsync(Ct).AsTask(), Ct)).ToArray();
        await Task.Delay(100, Ct);
        release.SetResult(Tokens("a", TimeSpan.FromDays(20)));
        var tokens = await Task.WhenAll(callers);

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));
        await _auth.Received(1).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_CurrentToken_RefreshesOnNextCall()
    {
        var first = Tokens("a", TimeSpan.FromDays(20));
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(first);
        _auth.RefreshAsync("refresh-a", Arg.Any<CancellationToken>()).Returns(Tokens("b", TimeSpan.FromDays(20)));
        var provider = CreateProvider();
        await provider.GetTokenAsync(Ct);

        provider.Invalidate(first.AccessToken);
        var token = await provider.GetTokenAsync(Ct);

        Assert.NotEqual(first.AccessToken, token, StringComparer.Ordinal);
        await _auth.Received(1).RefreshAsync("refresh-a", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_StaleToken_KeepsCurrent()
    {
        var first = Tokens("a", TimeSpan.FromDays(20));
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(first);
        _auth.RefreshAsync("refresh-a", Arg.Any<CancellationToken>()).Returns(Tokens("b", TimeSpan.FromDays(20)));
        var provider = CreateProvider();
        await provider.GetTokenAsync(Ct);
        provider.Invalidate(first.AccessToken);
        var current = await provider.GetTokenAsync(Ct);

        provider.Invalidate(first.AccessToken);
        var after = await provider.GetTokenAsync(Ct);

        Assert.Equal(current, after);
        await _auth.Received(1).RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneMinuteToken_LoginOnceThenReuse()
    {
        // R41: the skew is min(60 s, 10 % of the lifetime), so a 1-minute token is not expired on arrival.
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Tokens("a", TimeSpan.FromMinutes(1)));
        var provider = CreateProvider();

        await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromSeconds(30));
        await provider.GetTokenAsync(Ct);

        await _auth.Received(1).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnreadableExp_CachesFiveMinutes()
    {
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new AuthTokens("opaque-token", "refresh-x"));
        _auth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new AuthTokens("opaque-token-2", "refresh-y"));
        var provider = CreateProvider();

        await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(4));
        var cached = await provider.GetTokenAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(2));
        var renewed = await provider.GetTokenAsync(Ct);

        Assert.Equal("opaque-token", cached);
        Assert.Equal("opaque-token-2", renewed);
    }

    [Fact]
    public async Task Login_Hangs_TimesOutAndReleasesLock()
    {
        // R32: the AuthClient's own timeout ends the login; the lock is released, so the next caller can log in.
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<AuthTokens>(new UpstreamTimeoutException("login timed out")),
            _ => Task.FromResult(Tokens("a", TimeSpan.FromDays(20))));
        var provider = CreateProvider();

        await Assert.ThrowsAsync<UpstreamTimeoutException>(() => provider.GetTokenAsync(Ct).AsTask());
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        var token = await provider.GetTokenAsync(guard.Token);

        Assert.Contains(".", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancelledDuringLogin_LoginStillCachesToken()
    {
        // Review B I2: the login must not run on the first caller's token. That token carries the caller's attempt
        // timeout (shorter than AuthTimeoutSeconds) and its client abort; either would cancel the login for everyone.
        var release = new TaskCompletionSource<AuthTokens>(TaskCreationOptions.RunContinuationsAsynchronously);
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => release.Task.WaitAsync(call.Arg<CancellationToken>()));
        var provider = CreateProvider();
        using var firstCaller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var first = provider.GetTokenAsync(firstCaller.Token).AsTask();
        await firstCaller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var tokens = Tokens("a", TimeSpan.FromDays(20));
        release.SetResult(tokens);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        var token = await provider.GetTokenAsync(guard.Token);

        Assert.Equal(tokens.AccessToken, token);
        await _auth.Received(1).LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tokens_AreNeverLogged()
    {
        var tokens = Tokens("a", TimeSpan.FromDays(20));
        _auth.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(tokens);

        await CreateProvider().GetTokenAsync(Ct);

        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(tokens.AccessToken, StringComparison.Ordinal)
            || e.Message.Contains(tokens.RefreshToken, StringComparison.Ordinal)
            || e.Message.Contains("test-password", StringComparison.Ordinal));
    }
}
