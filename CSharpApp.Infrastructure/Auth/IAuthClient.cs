namespace CSharpApp.Infrastructure.Auth;

/// <summary>The upstream login and refresh calls (no bearer, no retry).</summary>
public interface IAuthClient
{
    /// <summary>Logs in with e-mail and password.</summary>
    Task<AuthTokens> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>Exchanges a refresh token for a new token pair.</summary>
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct);
}
