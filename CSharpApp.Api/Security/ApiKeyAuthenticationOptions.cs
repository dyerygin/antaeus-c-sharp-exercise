using Microsoft.AspNetCore.Authentication;

namespace CSharpApp.Api.Security;

/// <summary>The <c>ApiKey</c> authentication scheme: names shared by the handler, the policy and OpenAPI (ADR-0012).</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The scheme name, also sent in <c>WWW-Authenticate</c>.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>The request header that carries the key.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>The claim that names the authenticated client.</summary>
    public const string ClientIdClaim = "client_id";
}
