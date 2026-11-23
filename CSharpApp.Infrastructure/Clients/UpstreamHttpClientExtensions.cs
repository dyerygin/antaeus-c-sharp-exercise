using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>One upstream call: send, map the status, read a required JSON body, and turn transport and contract failures into <see cref="UpstreamException"/>s (ADR-0005).</summary>
internal static class UpstreamHttpClientExtensions
{
    extension(HttpClient client)
    {
        /// <summary>GET a required JSON body; headers first, then the body is streamed (F-09).</summary>
        public Task<T> GetRequiredAsync<T>(Uri uri, CancellationToken ct)
            where T : class =>
            CallAsync<T>(token => client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token), ct);

        /// <summary>POST a camelCase JSON body and read the required JSON answer; never retried (ADR-0004).</summary>
        public Task<T> PostRequiredAsync<TBody, T>(Uri uri, TBody body, CancellationToken ct)
            where T : class =>
            CallAsync<T>(token => client.PostAsJsonAsync(uri, body, JsonSerializerOptions.Web, token), ct);
    }

    private static async Task<T> CallAsync<T>(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await send(ct);
            await response.EnsureUpstreamSuccessAsync(ct);
            return await response.ReadRequiredJsonAsync<T>(ct);
        }
        catch (HttpRequestException ex)
        {
            // Connection refused, DNS, TLS or a broken stream: the caller sees 502, the log keeps the cause (F-07).
            throw new UpstreamUnavailableException("The upstream service could not be reached.", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // F-08: a 2xx with an empty, null or non-contract body is an upstream defect, not an empty result.
            throw new UpstreamContractException("The upstream service returned a body that does not match the contract.", ex);
        }
    }
}
