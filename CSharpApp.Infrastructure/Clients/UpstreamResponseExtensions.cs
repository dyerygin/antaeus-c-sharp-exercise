using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Reading upstream responses: status first, then a streamed, required JSON body (F-08, F-09).</summary>
internal static class UpstreamResponseExtensions
{
    extension(HttpResponseMessage response)
    {
        /// <summary>Deserializes the body; a missing or <c>null</c> body is a contract violation, not an empty result.</summary>
        public async Task<T> ReadRequiredJsonAsync<T>(CancellationToken ct)
            where T : class =>
            await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, ct)
                ?? throw new JsonException($"The upstream returned an empty {typeof(T).Name} body.");
    }
}
