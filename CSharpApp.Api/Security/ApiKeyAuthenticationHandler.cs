using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Security;

/// <summary>Authenticates a request by its <c>X-Api-Key</c> header (ADR-0012).</summary>
/// <remarks>
/// No header means no result, so public GET endpoints are unaffected; a wrong key fails. The key is never logged or
/// echoed. The challenge only sets 401 and <c>WWW-Authenticate: ApiKey</c>; status-code pages write the ProblemDetails.
/// </remarks>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiAuthSettings> settings)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers[ApiKeyAuthenticationOptions.HeaderName].ToString();
        if (presented.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Every configured key is compared (ToList), so the time does not depend on which one matched.
        var matches = settings.Value.Keys.Where(client => ApiKeyComparer.Matches(presented, client.Key)).ToList();
        var match = matches.Count > 0 ? matches[0] : null;

        if (match is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ApiKeyAuthenticationOptions.ClientIdClaim, match.ClientId)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyAuthenticationOptions.Scheme;
        return Task.CompletedTask;
    }
}
