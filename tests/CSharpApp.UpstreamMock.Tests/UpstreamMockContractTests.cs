using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.UpstreamMock.Tests;

/// <summary>The upstream contract (ASM-01) as observed on the original backend, executed against the mock.</summary>
public sealed class UpstreamMockContractTests : IAsyncDisposable
{
    private static readonly object ValidLogin = new { email = "john@mail.com", password = "changeme" };

    private readonly WebApplicationFactory<Program> _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Login_ValidCredentials_Returns201WithTokens()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/auth/login", ValidLogin, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("refresh_token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/auth/login", new { email = "john@mail.com", password = "wrong" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(401, body.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns201WithNewPair()
    {
        using var client = _factory.CreateClient();
        var (_, refresh) = await LoginAsync(client);

        using var response = await client.PostAsJsonAsync("api/v1/auth/refresh-token", new { refreshToken = refresh }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/auth/refresh-token", new { refreshToken = "not-a-token" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("Invalid", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("Unauthorized", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Refresh_AccessTokenInsteadOfRefreshToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var (access, _) = await LoginAsync(client);

        using var response = await client.PostAsJsonAsync("api/v1/auth/refresh-token", new { refreshToken = access }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("api/v1/auth/profile", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_WithToken_Returns200WithUser()
    {
        using var client = _factory.CreateClient();
        var (access, _) = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        using var response = await client.GetAsync(new Uri("api/v1/auth/profile", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("john@mail.com", body.RootElement.GetProperty("email").GetString());
        Assert.False(body.RootElement.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Profile_TokenSignedWithAnotherKey_Returns401()
    {
        using var client = _factory.CreateClient();
        var (access, _) = await LoginAsync(client);
        var tampered = access[..^2] + (access.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        using var response = await client.GetAsync(new Uri("api/v1/auth/profile", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Products_WithoutToken_Returns200_ByDefault()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("api/v1/products", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.GetArrayLength() >= 10);
    }

    [Fact]
    public async Task Products_WithoutToken_Returns401_WhenRequireTokenOnResources()
    {
        await using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("MockAuth:RequireTokenOnResources", "true"));
        using var client = factory.CreateClient();

        using var anonymous = await client.GetAsync(new Uri("api/v1/products", UriKind.Relative), Ct);
        var (access, _) = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var authenticated = await client.GetAsync(new Uri("api/v1/categories", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task Products_OffsetAndLimit_ReturnsPage()
    {
        using var client = _factory.CreateClient();

        using var all = await ReadJsonAsync(await client.GetAsync(new Uri("api/v1/products", UriKind.Relative), Ct));
        using var page = await ReadJsonAsync(await client.GetAsync(new Uri("api/v1/products?offset=2&limit=3", UriKind.Relative), Ct));

        Assert.Equal(3, page.RootElement.GetArrayLength());
        Assert.Equal(all.RootElement[2].GetProperty("id").GetInt32(), page.RootElement[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetProduct_KnownId_Returns200WithCategory()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("api/v1/products/1", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.RootElement.GetProperty("id").GetInt32());
        Assert.True(body.RootElement.GetProperty("category").GetProperty("id").GetInt32() > 0);
        Assert.True(body.RootElement.GetProperty("images").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetProduct_UnknownId_Returns400EntityNotFound()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("api/v1/products/9999", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("EntityNotFoundError", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetCategory_UnknownId_Returns400EntityNotFound()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("api/v1/categories/9999", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("EntityNotFoundError", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateProduct_Invalid_Returns400MessageArray()
    {
        using var client = _factory.CreateClient();
        var invalid = new { title = "", price = -1, description = "", categoryId = 1, images = Array.Empty<string>() };

        using var response = await client.PostAsJsonAsync("api/v1/products", invalid, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("message").ValueKind);
        Assert.True(body.RootElement.GetProperty("message").GetArrayLength() >= 4);
        Assert.Equal("Bad Request", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(400, body.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_Returns400EntityNotFound()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/products", NewProduct(price: 10m, categoryId: 9999), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal("EntityNotFoundError", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateProduct_FractionalPrice_EchoesInput_StoresInteger()
    {
        using var client = _factory.CreateClient();

        using var created = await client.PostAsJsonAsync("api/v1/products", NewProduct(price: 10.5m, categoryId: 1), Ct);
        using var createdBody = await ReadJsonAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetInt32();
        using var stored = await ReadJsonAsync(await client.GetAsync(new Uri($"api/v1/products/{id}", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(10.5m, createdBody.RootElement.GetProperty("price").GetDecimal());
        Assert.Equal("10", stored.RootElement.GetProperty("price").GetRawText());
    }

    [Fact]
    public async Task CreateCategory_Valid_Returns201AndIsListed()
    {
        using var client = _factory.CreateClient();

        using var created = await client.PostAsJsonAsync("api/v1/categories", new { name = "Books", image = "https://example.com/books.png" }, Ct);
        using var createdBody = await ReadJsonAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetInt32();
        using var fetched = await client.GetAsync(new Uri($"api/v1/categories/{id}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_Invalid_Returns400MessageArray()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("api/v1/categories", new { name = "", image = "not a url" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(2, body.RootElement.GetProperty("message").GetArrayLength());
    }

    [Fact]
    public async Task HealthLive_Returns200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("health/live", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object NewProduct(decimal price, int categoryId) => new
    {
        title = "New Product",
        price,
        description = "A description",
        categoryId,
        images = new[] { "https://example.com/new.png" },
    };

    private static async Task<(string Access, string Refresh)> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("api/v1/auth/login", ValidLogin, Ct);
        response.EnsureSuccessStatusCode();
        using var body = await ReadJsonAsync(response);
        return (body.RootElement.GetProperty("access_token").GetString()!, body.RootElement.GetProperty("refresh_token").GetString()!);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Ct), cancellationToken: Ct);
}
