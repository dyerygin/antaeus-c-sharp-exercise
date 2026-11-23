using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>One upstream call: send, map the status, read a required JSON body, and turn transport, timeout and contract failures into <see cref="UpstreamException"/>s (ADR-0005).</summary>
internal static class UpstreamHttpClientExtensions
{
    extension(HttpClient client)
    {
        /// <summary>GET a required JSON body; headers first, then the body is streamed (F-09), all within <paramref name="deadline"/>.</summary>
        public Task<T> GetRequiredAsync<T>(Uri uri, TimeSpan deadline, CancellationToken ct)
            where T : class =>
            CallAsync<T>(token => client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token), deadline, ct);

        /// <summary>POST a camelCase JSON body and read the required JSON answer within <paramref name="deadline"/>; never retried (ADR-0004).</summary>
        public Task<T> PostRequiredAsync<TBody, T>(Uri uri, TBody body, TimeSpan deadline, CancellationToken ct)
            where T : class =>
            CallAsync<T>(token => client.PostAsJsonAsync(uri, body, JsonSerializerOptions.Web, token), deadline, ct);
    }

    private static async Task<T> CallAsync<T>(Func<CancellationToken, Task<HttpResponseMessage>> send, TimeSpan deadline, CancellationToken ct)
        where T : class
    {
        // The resilience timeouts end when the headers arrive; reading the streamed body is outside them, and
        // HttpClient.Timeout is infinite under the standard handler. One deadline covers headers and body.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(deadline);
        try
        {
            using var response = await send(timeout.Token);
            await response.EnsureUpstreamSuccessAsync(timeout.Token);
            return await response.ReadRequiredJsonAsync<T>(timeout.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Our deadline, not the caller: 504. A caller's own cancellation propagates unchanged (499).
            throw new UpstreamTimeoutException("The upstream service did not answer in time.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Connection refused, DNS, TLS, or a connection reset mid-body (HttpIOException is an IOException): 502 (F-07).
            throw new UpstreamUnavailableException("The upstream service could not be reached.", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // F-08: a 2xx with an empty, null or non-contract body is an upstream defect, not an empty result.
            throw new UpstreamContractException("The upstream service returned a body that does not match the contract.", ex);
        }
    }
}
