using System.Net;
using System.Text.Json;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Exceptions;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Clients;
using CSharpApp.TestData.Builders;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Clients;

public sealed class ProductsClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static RestApiSettings Settings { get; } = new()
    {
        BaseUrl = "http://upstream.test/api/v1/",
        Products = "products",
        Categories = "categories",
        Auth = "auth/login",
        RefreshToken = "auth/refresh-token",
        Profile = "auth/profile",
        Username = "user@test",
        Password = "test-password",
    };

    private static ProductsClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(Settings.BaseUrl) }, Options.Create(Settings), NullLogger<ProductsClient>.Instance);

    [Fact]
    public async Task GetAll_DeserializesProducts_IncludingImagesAndDecimalPrice()
    {
        var handler = StubHttpMessageHandler.Json("""[{"id":7,"title":"Hoodie","price":10.5,"description":"d","images":["a"],"category":{"id":1,"name":"Clothes","slug":"clothes"}}]""");

        var products = await CreateClient(handler).GetAllAsync(0, 20, Ct);

        var product = Assert.Single(products);
        Assert.Equal(7, product.Id);
        Assert.Equal(10.5m, product.Price);
        Assert.Equal(["a"], product.Images);
        Assert.Equal("clothes", product.Category?.Slug);
    }

    [Fact]
    public async Task GetAll_SendsOffsetAndLimit()
    {
        var handler = StubHttpMessageHandler.Json("[]");

        await CreateClient(handler).GetAllAsync(40, 20, Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/products?offset=40&limit=20", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task GetById_RequestsProductsSlashId()
    {
        var handler = StubHttpMessageHandler.Json(JsonSerializer.Serialize(new ProductBuilder().WithId(42).Build(), JsonSerializerOptions.Web));

        var product = await CreateClient(handler).GetByIdAsync(42, Ct);

        Assert.Equal(42, product.Id);
        Assert.Equal("/api/v1/products/42", Assert.Single(handler.Requests).Uri.PathAndQuery);
    }

    [Fact]
    public async Task Create_PostsCamelCaseBody()
    {
        var handler = StubHttpMessageHandler.Json(JsonSerializer.Serialize(new ProductBuilder().WithId(99).Build(), JsonSerializerOptions.Web), HttpStatusCode.Created);

        var created = await CreateClient(handler).CreateAsync(new NewProduct("Lamp", 10.5m, "A lamp", 3, ["https://images.test/lamp.png"]), Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/products", request.Uri.PathAndQuery);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Lamp", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(10.5m, body.RootElement.GetProperty("price").GetDecimal());
        Assert.Equal(3, body.RootElement.GetProperty("categoryId").GetInt32());
        Assert.Equal("https://images.test/lamp.png", body.RootElement.GetProperty("images")[0].GetString());
        Assert.Equal(99, created.Id);
    }

    [Fact]
    public async Task Deserialize_UtcTimestamp_KeepsOffset()
    {
        var handler = StubHttpMessageHandler.Json("""{"id":1,"title":"t","price":1,"creationAt":"2026-01-01T10:00:00.000Z"}""");

        var product = await CreateClient(handler).GetByIdAsync(1, Ct);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), product.CreationAt);
        Assert.Equal(TimeSpan.Zero, product.CreationAt!.Value.Offset);
    }

    [Fact]
    public async Task Deserialize_UnknownFields_Ignored()
    {
        var handler = StubHttpMessageHandler.Json("""{"id":1,"title":"t","slug":"t","price":1,"extra":{"nested":true}}""");

        var product = await CreateClient(handler).GetByIdAsync(1, Ct);

        Assert.Equal(1, product.Id);
    }

    [Fact]
    public async Task NullBody_MapsToContractError()
    {
        var handler = StubHttpMessageHandler.Json("null");

        await Assert.ThrowsAsync<UpstreamContractException>(() => CreateClient(handler).GetByIdAsync(1, Ct));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("[1,2]")]
    public async Task MalformedBody_MapsToContractError(string body)
    {
        var handler = StubHttpMessageHandler.Json(body);

        await Assert.ThrowsAsync<UpstreamContractException>(() => CreateClient(handler).GetByIdAsync(1, Ct));
    }

    [Fact]
    public async Task GetById_Upstream500_ThrowsUnavailable()
    {
        var handler = StubHttpMessageHandler.Json("""{"message":"boom"}""", HttpStatusCode.InternalServerError);

        var error = await Assert.ThrowsAsync<UpstreamUnavailableException>(() => CreateClient(handler).GetByIdAsync(1, Ct));

        Assert.DoesNotContain("boom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetById_Upstream400EntityNotFound_ThrowsNotFound()
    {
        var handler = StubHttpMessageHandler.Json("""{"name":"EntityNotFoundError","message":"Could not find"}""", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<UpstreamNotFoundException>(() => CreateClient(handler).GetByIdAsync(999, Ct));
    }

    [Fact]
    public async Task Create_Upstream400_ThrowsBadRequest()
    {
        var handler = StubHttpMessageHandler.Json("""{"message":["price must be a positive number"]}""", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<UpstreamBadRequestException>(() =>
            CreateClient(handler).CreateAsync(new NewProduct("Lamp", 1m, "A lamp", 1, ["https://images.test/lamp.png"]), Ct));
    }

    [Fact]
    public async Task GetAll_ConnectionRefused_ThrowsUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused"));

        var error = await Assert.ThrowsAsync<UpstreamUnavailableException>(() => CreateClient(handler).GetAllAsync(0, 20, Ct));

        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task GetAll_Cancelled_ThrowsOperationCanceled()
    {
        var handler = StubHttpMessageHandler.Json("[]");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).GetAllAsync(0, 20, cts.Token));
    }
}
