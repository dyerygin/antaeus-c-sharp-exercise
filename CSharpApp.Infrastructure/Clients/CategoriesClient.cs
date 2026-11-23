using System.Globalization;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Typed client for the upstream categories resource (ADR-0004).</summary>
public sealed partial class CategoriesClient(HttpClient httpClient, IOptions<RestApiSettings> settings, IOptions<HttpClientSettings> http, ILogger<CategoriesClient> logger) : ICategoriesClient
{
    private string Resource => settings.Value.Categories;

    private TimeSpan Deadline => TimeSpan.FromSeconds(http.Value.TotalTimeoutSeconds);

    public async Task<IReadOnlyList<Category>> GetAllAsync(int limit, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}?limit={limit}"), UriKind.Relative);
        var categories = await httpClient.GetRequiredAsync<List<Category>>(uri, Deadline, ct);
        LogFetchedList(logger, categories.Count, limit);
        return categories;
    }

    public async Task<Category> GetByIdAsync(int id, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}/{id}"), UriKind.Relative);
        var category = await httpClient.GetRequiredAsync<Category>(uri, Deadline, ct);
        LogFetchedOne(logger, id);
        return category;
    }

    public async Task<Category> CreateAsync(NewCategory category, CancellationToken ct)
    {
        var created = await httpClient.PostRequiredAsync<NewCategory, Category>(new Uri(Resource, UriKind.Relative), category, Deadline, ct);
        LogCreated(logger, created.Id);
        return created;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched {Count} categories (limit {Limit})")]
    private static partial void LogFetchedList(ILogger logger, int count, int limit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetched category {CategoryId}")]
    private static partial void LogFetchedOne(ILogger logger, int categoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created category {CategoryId} upstream")]
    private static partial void LogCreated(ILogger logger, int categoryId);
}
