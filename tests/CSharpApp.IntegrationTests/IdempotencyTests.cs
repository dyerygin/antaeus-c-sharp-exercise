using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CSharpApp.TestData.Builders;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace CSharpApp.IntegrationTests;

/// <summary>Idempotent creates with <c>Idempotency-Key</c> (ADR-0013, review focus 3, T1).</summary>
public sealed class IdempotencyTests : IAsyncDisposable
{
    private const string KeyHeader = "Idempotency-Key";

    private static readonly object CategoryBody = new { name = "Lamps", image = "https://images.test/c.png" };

    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private int UpstreamCreates(string path) =>
        _factory.Upstream.LogEntries.Count(e => string.Equals(e.RequestMessage?.Method, "POST", StringComparison.Ordinal)
            && string.Equals(e.RequestMessage?.Path, $"/api/v1/{path}", StringComparison.Ordinal));

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? key, CancellationToken ct) =>
        PostRawAsync(client, path, JsonSerializer.Serialize(body, JsonSerializerOptions.Web), key, ct);

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/{path}", UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.Add(KeyHeader, key);
        }

        return await client.SendAsync(request, ct);
    }

    private void StubProductCreate(int id) =>
        _factory.Upstream.StubJson("POST", "products", 201, new ProductBuilder().WithId(id).WithPrice(10.5m).Build());

    private static object ProductBody(string title = "Lamp") => new
    {
        title,
        price = 10.5,
        description = "A desk lamp",
        categoryId = 1,
        images = new[] { "https://images.test/lamp.png" },
    };

    [Fact]
    public async Task Create_SameKeyTwice_OneUpstreamCreate()
    {
        StubProductCreate(42);
        _factory.Upstream.StubJson("GET", "products/42", 200, new ProductBuilder().WithId(42).WithPrice(10m).Build());
        using var client = _factory.CreateClient();

        using var first = await PostAsync(client, "products", ProductBody(), "key-1", Ct);
        using var second = await PostAsync(client, "products", ProductBody(), "key-1", Ct);

        Assert.Equal(1, UpstreamCreates("products"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal("application/json", second.Content.Headers.ContentType?.MediaType);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues("Idempotent-Replayed")));
    }

    [Fact]
    public async Task Create_SameJsonDifferentWhitespace_Replays()
    {
        // ADR-0013: the hash covers the bound request, not the raw bytes, so formatting does not matter.
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(7).Build());
        using var client = _factory.CreateClient();

        using var first = await PostRawAsync(client, "categories", """{"name":"Lamps","image":"https://images.test/c.png"}""", "key-ws", Ct);
        using var second = await PostRawAsync(client, "categories", "{ \"name\" : \"Lamps\",\n  \"image\": \"https://images.test/c.png\" }", "key-ws", Ct);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, UpstreamCreates("categories"));
    }

    [Fact]
    public async Task Create_SameKeyDifferentBody_Returns422()
    {
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(7).Build());
        using var client = _factory.CreateClient();

        using var first = await PostAsync(client, "categories", CategoryBody, "key-2", Ct);
        using var second = await PostAsync(client, "categories", new { name = "Other", image = "https://images.test/c.png" }, "key-2", Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, UpstreamCreates("categories"));
    }

    [Fact]
    public async Task Create_SameKeyOtherEndpoint_IsIndependent()
    {
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(7).Build());
        StubProductCreate(42);
        _factory.Upstream.StubJson("GET", "products/42", 200, new ProductBuilder().WithId(42).Build());
        using var client = _factory.CreateClient();

        using var category = await PostAsync(client, "categories", CategoryBody, "shared-key", Ct);
        using var product = await PostAsync(client, "products", ProductBody(), "shared-key", Ct);

        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        Assert.Equal(HttpStatusCode.Created, product.StatusCode);
    }

    /// <summary>A WireMock answer that signals its arrival and then waits for the gate, so the test controls the overlap.</summary>
    private static WireMock.ResponseMessage GatedJson(TaskCompletionSource arrived, ManualResetEventSlim gate, int status, object body)
    {
        arrived.TrySetResult();
        gate.Wait(TimeSpan.FromSeconds(10), Ct);
        return new WireMock.ResponseMessage
        {
            StatusCode = status,
            Headers = new Dictionary<string, WireMock.Types.WireMockList<string>>(StringComparer.Ordinal) { ["Content-Type"] = new("application/json") },
            BodyData = new WireMock.Util.BodyData
            {
                BodyAsString = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
                DetectedBodyType = WireMock.Types.BodyType.String,
            },
        };
    }

    [Fact]
    public async Task Create_ConcurrentSameKey_OneSucceedsOneReturns409()
    {
        // R31: the upstream create is held open until the second request has been answered, so the two truly overlap.
        using var gate = new ManualResetEventSlim();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingPost())
            .RespondWith(Response.Create().WithCallback(_ => GatedJson(arrived, gate, 201, new CategoryBuilder().WithId(7).Build())));
        using var client = _factory.CreateClient();

        var first = PostAsync(client, "categories", CategoryBody, "key-3", Ct);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var second = await PostAsync(client, "categories", CategoryBody, "key-3", Ct);
        gate.Set();
        using var firstResponse = await first;

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(1, UpstreamCreates("categories"));
    }

    [Fact]
    public async Task Create_UpstreamFails_KeyCanBeRetried()
    {
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingPost())
            .InScenario("fails-once").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(500));
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/categories").UsingPost())
            .InScenario("fails-once").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(201).WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new CategoryBuilder().WithId(7).Build(), JsonSerializerOptions.Web)));
        using var client = _factory.CreateClient();

        using var failed = await PostAsync(client, "categories", CategoryBody, "key-4", Ct);
        using var retried = await PostAsync(client, "categories", CategoryBody, "key-4", Ct);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(2, UpstreamCreates("categories"));
    }

    [Fact]
    public async Task Create_NoKey_BehavesNormally()
    {
        _factory.Upstream.StubJson("POST", "categories", 201, new CategoryBuilder().WithId(7).Build());
        using var client = _factory.CreateClient();

        using var first = await PostAsync(client, "categories", CategoryBody, null, Ct);
        using var second = await PostAsync(client, "categories", CategoryBody, null, Ct);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, UpstreamCreates("categories"));
    }

    [Theory]
    [InlineData("key with space")]
    [InlineData("key\twith-tab")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789x")]
    public async Task Create_InvalidKey_Returns400(string key)
    {
        using var client = _factory.CreateClient();

        using var response = await PostAsync(client, "categories", CategoryBody, key, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(KeyHeader, out _));
        Assert.Empty(_factory.Upstream.ResourceEntries);
    }

    [Fact]
    public async Task Create_ClientAbortsAfterUpstreamSuccess_RetryReplays()
    {
        // T1: the client gives up while the read-back is held open; the create happened, so the result is still stored.
        StubProductCreate(42);
        using var gate = new ManualResetEventSlim();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Upstream.Given(Request.Create().WithPath("/api/v1/products/42").UsingGet())
            .RespondWith(Response.Create().WithCallback(_ => GatedJson(arrived, gate, 200, new ProductBuilder().WithId(42).WithPrice(10m).Build())));
        using var client = _factory.CreateClient();
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var aborted = PostAsync(client, "products", ProductBody(), "key-5", abort.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await abort.CancelAsync();
        // TestServer completes the aborted call only when the server is done, so the read-back is released now.
        gate.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => aborted);

        HttpResponseMessage retried;
        do
        {
            // The aborted request finishes on the server shortly after; until then the key is in flight (409).
            retried = await PostAsync(client, "products", ProductBody(), "key-5", Ct);
        }
        while (retried.StatusCode == HttpStatusCode.Conflict && await PauseAsync());

        using (retried)
        {
            Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
            Assert.Equal("true", Assert.Single(retried.Headers.GetValues("Idempotent-Replayed")));
            Assert.Equal(10m, JsonDocument.Parse(await retried.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("price").GetDecimal());
        }

        Assert.Equal(1, UpstreamCreates("products"));
    }

    private static async Task<bool> PauseAsync()
    {
        await Task.Delay(50, Ct);
        return true;
    }
}
