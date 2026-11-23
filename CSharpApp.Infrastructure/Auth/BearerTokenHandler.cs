using System.Net;
using System.Net.Http.Headers;

namespace CSharpApp.Infrastructure.Auth;

/// <summary>Sends the cached upstream access token as a Bearer header and resends once after a 401 (ADR-0014).</summary>
/// <remarks>
/// The upstream answers 401 before it processes a request, so a single resend is safe even for POST: the one stated
/// exception to "never retry POST" (ADR-0004, ADR-0014). A second 401 is returned as is and the client maps it to 502
/// (ADR-0005), so a revoked account cannot cause a loop. Sits below the resilience handler, so each attempt gets a
/// fresh token and the 401 resend counts inside that attempt's timeout.
/// </remarks>
public sealed class BearerTokenHandler(IAccessTokenProvider tokens) : DelegatingHandler
{
    private const string Scheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffered before the first send, so the resend carries exactly the same bytes (ADR-0014).
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var token = await tokens.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        tokens.Invalidate(token);
        var renewed = await tokens.GetTokenAsync(cancellationToken);

        var resend = Clone(request, body);
        resend.Headers.Authorization = new AuthenticationHeaderValue(Scheme, renewed);
        // The resend is owned by its response (RequestMessage) and released with it.
        return await base.SendAsync(resend, cancellationToken);
    }

    /// <summary>A copy of the request with its headers, options and buffered body; the original is already sent.</summary>
    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in request.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
