using System.Globalization;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Typed client for the upstream products resource (ADR-0004).</summary>
public sealed partial class ProductsClient(HttpClient httpClient, IOptions<RestApiSettings> settings, IOptions<HttpClientSettings> http, ILogger<ProductsClient> logger) : IProductsClient
{
    private string Resource => settings.Value.Products;

    private TimeSpan Deadline => TimeSpan.FromSeconds(http.Value.TotalTimeoutSeconds);

    public async Task<IReadOnlyList<Product>> GetAllAsync(int offset, int limit, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}?offset={offset}&limit={limit}"), UriKind.Relative);
        var products = await httpClient.GetRequiredAsync<List<Product>>(uri, Deadline, ct);
        LogFetchedPage(logger, products.Count, offset, limit);
        return products;
    }

    public async Task<Product> GetByIdAsync(int id, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}/{id}"), UriKind.Relative);
        var product = await httpClient.GetRequiredAsync<Product>(uri, Deadline, ct);
        LogFetchedOne(logger, id);
        return product;
    }

    public async Task<Product> CreateAsync(NewProduct product, CancellationToken ct)
    {
        var created = await httpClient.PostRequiredAsync<NewProduct, Product>(new Uri(Resource, UriKind.Relative), product, Deadline, ct);
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
