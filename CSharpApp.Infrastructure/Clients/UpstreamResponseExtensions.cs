using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Reading upstream responses: status first, then a streamed, required JSON body (F-08, F-09, ADR-0005).</summary>
internal static class UpstreamResponseExtensions
{
    // ASM-10: the upstream reports an unknown id (or an unknown categoryId on create) as 400 with this error name.
    private const string EntityNotFoundError = "EntityNotFoundError";

    extension(HttpResponseMessage response)
    {
        /// <summary>Turns a non-success status into the matching <see cref="UpstreamException"/>.</summary>
        /// <remarks>
        /// 404 or 400 <c>EntityNotFoundError</c> → not found; other 400/422 → bad request (the upstream message is kept
        /// for logs only); 401/403 (our credentials), 3xx (redirects are not followed, ADR-0004) and everything else →
        /// unavailable (ADR-0005).
        /// </remarks>
        public async Task EnsureUpstreamSuccessAsync(CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var status = response.StatusCode;
            if (status == HttpStatusCode.NotFound)
            {
                throw new UpstreamNotFoundException("The upstream service has no such entity.");
            }

            if (status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                var (name, message) = await ReadErrorAsync(response, ct);
                if (string.Equals(name, EntityNotFoundError, StringComparison.Ordinal))
                {
                    throw new UpstreamNotFoundException("The upstream service has no such entity.");
                }

                throw new UpstreamBadRequestException(message);
            }

            throw new UpstreamUnavailableException(string.Create(
                CultureInfo.InvariantCulture, $"The upstream service answered {(int)status}."));
        }

        /// <summary>Deserializes the body; a missing or <c>null</c> body is a contract violation, not an empty result.</summary>
        public async Task<T> ReadRequiredJsonAsync<T>(CancellationToken ct)
            where T : class =>
            await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, ct)
                ?? throw new JsonException($"The upstream returned an empty {typeof(T).Name} body.");
    }

    /// <summary>Reads <c>name</c> and <c>message</c> (a string or an array of strings) from an error body; unreadable → nulls.</summary>
    private static async Task<(string? Name, string? Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? message = null;
            if (root.TryGetProperty("message", out var m))
            {
                message = m.ValueKind switch
                {
                    JsonValueKind.String => m.GetString(),
                    JsonValueKind.Array => string.Join("; ", m.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())),
                    _ => null,
                };
            }

            return (name, message);
        }
        catch (JsonException)
        {
            // An error body that is not JSON still means "rejected"; only its text is lost.
            return (null, null);
        }
    }
}
