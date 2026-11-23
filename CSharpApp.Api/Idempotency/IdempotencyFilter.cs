using System.Security.Cryptography;
using System.Text.Json;
using CSharpApp.Api.Security;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Idempotency;

/// <summary>Makes a create endpoint idempotent per <c>Idempotency-Key</c> (ADR-0013).</summary>
/// <typeparam name="TRequest">The endpoint's body type; its bound value is what the key is tied to.</typeparam>
/// <remarks>
/// Without the header nothing changes. With it: a new key runs the endpoint and stores a 2xx response; the same key and
/// body replays it without running the endpoint; another body gets 422; a key still running gets 409. Errors are not
/// stored, so the client can retry. The response is stored with <see cref="CancellationToken.None"/>, so a client that
/// disconnects after the upstream create still finds it on retry.
/// </remarks>
public sealed class IdempotencyFilter<TRequest>(IIdempotencyStore store, IOptions<JsonOptions> json) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(IdempotencyHeaders.Key, out var values)
            || context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return await next(context);
        }

        var key = values.ToString();
        if (!IdempotencyHeaders.IsValidKey(key))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [IdempotencyHeaders.Key] = [$"Must be 1 to {IdempotencyHeaders.MaxKeyLength} visible ASCII characters, without spaces."],
            });
        }

        // The key belongs to one client and one endpoint; '\n' cannot occur in a header value, so parts cannot run together.
        var scope = string.Join('\n',
            http.User.FindFirst(ApiKeyAuthenticationOptions.ClientIdClaim)?.Value ?? "anonymous",
            http.Request.Method,
            (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value,
            key);

        // ADR-0013: the hash covers the bound request re-serialized, so whitespace and member order do not matter.
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, json.Value.SerializerOptions)));

        var lookup = await store.TryBeginAsync(scope, hash, http.RequestAborted);
        switch (lookup.Outcome)
        {
            case IdempotencyOutcome.HashMismatch:
                return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "This Idempotency-Key was already used with a different request body.");
            case IdempotencyOutcome.InProgress:
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "A request with this Idempotency-Key is still in progress.");
            case IdempotencyOutcome.Replay:
                return new StoredResponseResult(lookup.Response!, replayed: true);
            default:
                return await RunAndStoreAsync(context, next, scope);
        }
    }

    private async Task<object?> RunAndStoreAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, string scope)
    {
        var http = context.HttpContext;
        StoredResponse? stored = null;
        try
        {
            if (await next(context) is not IResult result)
            {
                return null;
            }

            // Run the result into a buffer: what is stored is exactly what the client gets. The JSON writer skips
            // the body once the client has gone, so the buffered run does not see the client's abort.
            var original = (http.Response.Body, http.RequestAborted);
            using var buffer = new MemoryStream();
            http.Response.Body = buffer;
            http.RequestAborted = CancellationToken.None;
            try
            {
                await result.ExecuteAsync(http);
            }
            finally
            {
                (http.Response.Body, http.RequestAborted) = original;
            }

            stored = new StoredResponse(http.Response.StatusCode, http.Response.Headers.Location, buffer.ToArray(),
                http.Response.ContentType ?? "application/json");
            return new StoredResponseResult(stored, replayed: false);
        }
        finally
        {
            if (stored is { StatusCode: >= 200 and < 300 })
            {
                await store.CompleteAsync(scope, stored, CancellationToken.None);
            }
            else
            {
                await store.AbandonAsync(scope, CancellationToken.None);
            }
        }
    }
}
