using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace CSharpApp.IntegrationTests;

/// <summary>
/// Unreadable, ambiguous or oversized bodies are rejected before validation and before any upstream call, with the same
/// status in Development (ThrowOnBadRequest) and Production (R3, R23, R43, ADR-0006).
/// </summary>
public sealed class RequestBodyTests : IAsyncLifetime
{
    private const string Valid = """{"title":"Lamp","price":10.5,"description":"A lamp","categoryId":1,"images":["https://images.test/lamp.png"]}""";

    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<HttpResponseMessage> PostAsync(string environment, string body, string mediaType = "application/json")
    {
        using var client = _factory.WithWebHostBuilder(b => b.UseEnvironment(environment)).CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        return await client.PostAsync(new Uri("/api/v1/products", UriKind.Relative), content, Ct);
    }

    public static TheoryData<string> Environments() => ["Development", "Production"];

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task MalformedJson_Returns400(string environment)
    {
        using var response = await PostAsync(environment, """{"title":"Lamp",""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task WrongContentType_Returns415(string environment)
    {
        using var response = await PostAsync(environment, Valid, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task DuplicateJsonProperty_Returns400(string environment)
    {
        using var response = await PostAsync(environment, Valid.Replace("\"price\":10.5", "\"price\":10.5,\"price\":0.5", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task UnknownProperty_Returns400(string environment)
    {
        using var response = await PostAsync(environment, Valid.Replace("{", """{"isAdmin":true,""", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task NullInNonNullableProperty_Returns400(string environment)
    {
        using var response = await PostAsync(environment, Valid.Replace("\"Lamp\"", "null", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task NumberAsString_Returns400()
    {
        using var response = await PostAsync("Development", Valid.Replace("10.5", "\"10.5\"", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }

    [Fact]
    public async Task BodyTooLarge_Returns413()
    {
        // TestServer does not enforce Kestrel's MaxRequestBodySize, so this runs on a real Kestrel listener (R3).
        _factory.UseKestrel(0);
        _factory.StartServer();
        using var client = _factory.CreateClient();
        using var content = new StringContent(Valid.Replace("A lamp", new string('x', 70_000), StringComparison.Ordinal), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/products", UriKind.Relative), content, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_factory.Upstream.LogEntries);
    }
}
