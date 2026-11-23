using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Clients;

/// <summary>Typed client for the upstream categories resource (ADR-0004).</summary>
public sealed partial class CategoriesClient(HttpClient httpClient, IOptions<RestApiSettings> settings, ILogger<CategoriesClient> logger) : ICategoriesClient
{
    private string Resource => settings.Value.Categories;

    public async Task<IReadOnlyList<Category>> GetAllAsync(int limit, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}?limit={limit}"), UriKind.Relative);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var categories = await response.ReadRequiredJsonAsync<List<Category>>(ct);
        LogFetchedList(logger, categories.Count, limit);
        return categories;
    }

    public async Task<Category> GetByIdAsync(int id, CancellationToken ct)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"{Resource}/{id}"), UriKind.Relative);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var category = await response.ReadRequiredJsonAsync<Category>(ct);
        LogFetchedOne(logger, id);
        return category;
    }

    public async Task<Category> CreateAsync(NewCategory category, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync(new Uri(Resource, UriKind.Relative), category, JsonSerializerOptions.Web, ct);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadRequiredJsonAsync<Category>(ct);
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
