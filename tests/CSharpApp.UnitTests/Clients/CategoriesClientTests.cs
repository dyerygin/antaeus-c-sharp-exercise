using System.Net;
using System.Text.Json;
using CSharpApp.Core.Dtos;
using CSharpApp.Infrastructure.Clients;
using CSharpApp.TestData.Builders;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Clients;

public sealed class CategoriesClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CategoriesClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(ProductsClientTests.Settings.BaseUrl) }, Options.Create(ProductsClientTests.Settings), NullLogger<CategoriesClient>.Instance);

    [Fact]
    public async Task GetAll_DeserializesCategories_IncludingSlug()
    {
        var handler = StubHttpMessageHandler.Json("""[{"id":2,"name":"Electronics","slug":"electronics","image":"https://images.test/e.png","creationAt":"2026-01-01T10:00:00.000Z"}]""");

        var categories = await CreateClient(handler).GetAllAsync(5, Ct);

        var category = Assert.Single(categories);
        Assert.Equal(2, category.Id);
        Assert.Equal("electronics", category.Slug);
        Assert.Equal(TimeSpan.Zero, category.CreationAt!.Value.Offset);
    }

    [Fact]
    public async Task GetAll_SendsLimit()
    {
        var handler = StubHttpMessageHandler.Json("[]");

        await CreateClient(handler).GetAllAsync(5, Ct);

        Assert.Equal("/api/v1/categories?limit=5", Assert.Single(handler.Requests).Uri.PathAndQuery);
    }

    [Fact]
    public async Task GetById_RequestsCategoriesSlashId()
    {
        var handler = StubHttpMessageHandler.Json(JsonSerializer.Serialize(new CategoryBuilder().WithId(3).Build(), JsonSerializerOptions.Web));

        var category = await CreateClient(handler).GetByIdAsync(3, Ct);

        Assert.Equal(3, category.Id);
        Assert.Equal("/api/v1/categories/3", Assert.Single(handler.Requests).Uri.PathAndQuery);
    }

    [Fact]
    public async Task Create_PostsCamelCaseBody()
    {
        var handler = StubHttpMessageHandler.Json(JsonSerializer.Serialize(new CategoryBuilder().WithId(6).WithName("Books").Build(), JsonSerializerOptions.Web), HttpStatusCode.Created);

        var created = await CreateClient(handler).CreateAsync(new NewCategory("Books", "https://images.test/books.png"), Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/categories", request.Uri.PathAndQuery);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Books", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("https://images.test/books.png", body.RootElement.GetProperty("image").GetString());
        Assert.Equal(6, created.Id);
    }

    [Fact]
    public async Task GetById_NullBody_ThrowsJsonException()
    {
        var handler = StubHttpMessageHandler.Json("null");

        await Assert.ThrowsAsync<JsonException>(() => CreateClient(handler).GetByIdAsync(1, Ct));
    }
}
