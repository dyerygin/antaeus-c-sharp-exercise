using CSharpApp.Infrastructure.Clients;

namespace CSharpApp.Infrastructure.Auth;

/// <summary>Typed client for <c>auth/login</c> and <c>auth/refresh-token</c>: no bearer, no retry, its own timeout (ADR-0014).</summary>
public sealed class AuthClient(HttpClient httpClient, IOptions<RestApiSettings> settings, IOptions<HttpClientSettings> http) : IAuthClient
{
    private TimeSpan Deadline => TimeSpan.FromSeconds(http.Value.AuthTimeoutSeconds);

    /// <remarks>The setting <c>Username</c> is the upstream's <c>email</c> field (F-19). Accepts 200 and 201.</remarks>
    public Task<AuthTokens> LoginAsync(string email, string password, CancellationToken ct) =>
        httpClient.PostRequiredAsync<LoginBody, AuthTokens>(
            new Uri(settings.Value.Auth, UriKind.Relative), new LoginBody(email, password), Deadline, ct);

    public Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        httpClient.PostRequiredAsync<RefreshBody, AuthTokens>(
            new Uri(settings.Value.RefreshToken, UriKind.Relative), new RefreshBody(refreshToken), Deadline, ct);

    private sealed record LoginBody(string Email, string Password);

    private sealed record RefreshBody(string RefreshToken);
}
