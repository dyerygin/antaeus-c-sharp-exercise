using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Typed client for the upstream products resource (ADR-0004).</summary>
public sealed partial class ProductsClient(HttpClient httpClient, IOptions<RestApiSettings> settings, ILogger<ProductsClient> logger) : IProductsClient
{
    private string Resource => settings.Value.Products;

    public async Task<IReadOnlyList<Product>> GetAllAsync(int offset, int limit, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}?offset={offset}&limit={limit}"), UriKind.Relative);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var products = await response.ReadRequiredJsonAsync<List<Product>>(ct);
        LogFetchedPage(logger, products.Count, offset, limit);
        return products;
    }

    public async Task<Product> GetByIdAsync(int id, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}/{id}"), UriKind.Relative);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var product = await response.ReadRequiredJsonAsync<Product>(ct);
        LogFetchedOne(logger, id);
        return product;
    }

    public async Task<Product> CreateAsync(NewProduct product, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(new Uri(Resource, UriKind.Relative), product, JsonSerializerOptions.Web, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadRequiredJsonAsync<Product>(ct);
        LogCreated(logger, created.Id);
        return created;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched {Count} products (offset {Offset}, limit {Limit})")]
    private static partial void LogFetchedPage(ILogger logger, int count, int offset, int limit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched product {ProductId}")]
    private static partial void LogFetchedOne(ILogger logger, int productId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created product {ProductId} upstream")]
    private static partial void LogCreated(ILogger logger, int productId);
}
