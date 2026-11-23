namespace CSharpApp.Api.Idempotency;

/// <summary>Writes a <see cref="StoredResponse"/>; a replay also gets <c>Idempotent-Replayed: true</c>.</summary>
internal sealed class StoredResponseResult(StoredResponse response, bool replayed) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var http = httpContext.Response;
        http.StatusCode = response.StatusCode;
        http.ContentType = response.ContentType;
        if (response.Location is not null)
        {
            http.Headers.Location = response.Location;
        }

        if (replayed)
        {
            http.Headers[IdempotencyHeaders.Replayed] = "true";
        }

        await http.Body.WriteAsync(response.Body, httpContext.RequestAborted);
    }
}
