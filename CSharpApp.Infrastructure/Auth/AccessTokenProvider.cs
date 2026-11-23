namespace CSharpApp.Infrastructure.Auth;

/// <summary>Caches the upstream access token; one login for concurrent callers; refresh first, then login (ADR-0014).</summary>
/// <remarks>
/// A singleton. It holds the typed <see cref="IAuthClient"/> for its lifetime, which is safe because the primary handler
/// recycles pooled connections every 2 minutes (ADR-0004). Tokens and credentials are never logged.
/// </remarks>
public sealed partial class AccessTokenProvider(IAuthClient auth, IOptions<RestApiSettings> settings, TimeProvider time, ILogger<AccessTokenProvider> logger)
    : IAccessTokenProvider
{
    // Without a readable exp the token is reused this long; the upstream's tokens normally last 20 days (ASM-04).
    private static readonly TimeSpan UnknownLifetime = TimeSpan.FromMinutes(5);

    // ADR-0014: renew min(60 s, 10 % of the lifetime) early, so short-lived tokens are not expired on arrival.
    private static readonly TimeSpan MaxSkew = TimeSpan.FromSeconds(60);

    private readonly Lock _sync = new();
    private CachedToken? _current;
    private Task<CachedToken>? _renewal;

    public async ValueTask<string> GetTokenAsync(CancellationToken ct)
    {
        if (FreshToken(Volatile.Read(ref _current)) is { } cached)
        {
            return cached;
        }

        Task<CachedToken> renewal;
        lock (_sync)
        {
            // Double check: another caller may have renewed the token already (one login for many).
            if (FreshToken(_current) is { } renewed)
            {
                return renewed;
            }

            // One shared renewal that runs on no caller's token. A caller's token carries its attempt
            // timeout and its client abort, which would cancel the login for every waiter; AuthClient bounds the
            // call with its own timeout. A finished (also a failed) renewal is replaced, so the next caller retries.
            if (_renewal is null || _renewal.IsCompleted)
            {
                _renewal = RenewAsync(_current);
            }

            renewal = _renewal;
        }

        // Each caller stops waiting on its own token; the renewal goes on for the others.
        return (await renewal.WaitAsync(ct)).AccessToken;
    }

    public void Invalidate(string token)
    {
        var current = Volatile.Read(ref _current);
        if (current is not null && string.Equals(current.AccessToken, token, StringComparison.Ordinal))
        {
            // Only the token that was rejected: a newer one, already cached by another caller, stays. The refresh
            // token is kept, so the next call refreshes before it falls back to a login.
            Interlocked.CompareExchange(ref _current, current with { RenewAt = DateTimeOffset.MinValue }, current);
            LogInvalidated(logger);
        }
    }

    private string? FreshToken(CachedToken? token) =>
        token is not null && time.GetUtcNow() < token.RenewAt ? token.AccessToken : null;

    private async Task<CachedToken> RenewAsync(CachedToken? current)
    {
        // Yield first, so the renewal is never completed inside the caller's lock.
        await Task.Yield();
        var tokens = current is null
            ? await LoginAsync(CancellationToken.None)
            : await RefreshOrLoginAsync(current.RefreshToken, CancellationToken.None);
        var next = Cache(tokens);
        Volatile.Write(ref _current, next);
        return next;
    }

    private async Task<AuthTokens> LoginAsync(CancellationToken ct)
    {
        var tokens = await auth.LoginAsync(settings.Value.Username, settings.Value.Password, ct);
        LogLoggedIn(logger);
        return tokens;
    }

    private async Task<AuthTokens> RefreshOrLoginAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var tokens = await auth.RefreshAsync(refreshToken, ct);
            LogRefreshed(logger);
            return tokens;
        }
        catch (UpstreamException ex)
        {
            // The refresh token lives 10 hours (ASM-04); after that, or if refresh fails at all, log in again.
            LogRefreshFailed(logger, ex.GetType().Name);
            return await LoginAsync(ct);
        }
    }

    private CachedToken Cache(AuthTokens tokens)
    {
        var now = time.GetUtcNow();
        var renewAt = JwtExpiry.Read(tokens.AccessToken) is { } expires
            ? expires - TimeSpan.FromTicks(Math.Min(MaxSkew.Ticks, Math.Max(0, (expires - now).Ticks / 10)))
            : now + UnknownLifetime;
        LogCached(logger, renewAt);
        return new CachedToken(tokens.AccessToken, tokens.RefreshToken, renewAt);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logged in to the upstream")]
    private static partial void LogLoggedIn(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshed the upstream token")]
    private static partial void LogRefreshed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream token refresh failed ({Reason}); logging in again")]
    private static partial void LogRefreshFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upstream token cached until {RenewAt}")]
    private static partial void LogCached(ILogger logger, DateTimeOffset renewAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "The upstream rejected the cached token; it will be renewed")]
    private static partial void LogInvalidated(ILogger logger);

    private sealed record CachedToken(string AccessToken, string RefreshToken, DateTimeOffset RenewAt);
}
