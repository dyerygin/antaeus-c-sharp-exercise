using System.Collections.Concurrent;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>An in-memory HTTP handler: answers with the given function and records every request (no network).</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    public static StubHttpMessageHandler Json(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body));
        return await respond(request, cancellationToken);
    }

    /// <summary>What the handler received; the body is read eagerly because the request is disposed afterwards.</summary>
    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);
}
