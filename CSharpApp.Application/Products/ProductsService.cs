// Base-commit service kept only until the typed upstream clients replace it (Removed in Task 5).
// Its known defects (shared mutated HttpClient F-01, null dereference F-08, interpolated log template)
// are fixed by that rewrite rather than patched here.
#pragma warning disable CA1001, CA1848, CA1873, CA2254, CS8602, S2629
namespace CSharpApp.Application.Products;

public class ProductsService : IProductsService
{
    private readonly HttpClient _httpClient;
    private readonly RestApiSettings _restApiSettings;
    private readonly ILogger<ProductsService> _logger;

    public ProductsService(IOptions<RestApiSettings> restApiSettings,
        ILogger<ProductsService> logger)
    {
        _httpClient = new HttpClient();
        _restApiSettings = restApiSettings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<Product>> GetProducts()
    {
        _httpClient.BaseAddress = new Uri(_restApiSettings.BaseUrl);
        var response = await _httpClient.GetAsync(_restApiSettings.Products);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var res = JsonSerializer.Deserialize<List<Product>>(content);
        foreach (var _ in res)
        {
            _logger.LogInformation($"Fetched : {res.Count} number of products");
        }
        return res.AsReadOnly();
    }
}
#pragma warning restore CA1001, CA1848, CA1873, CA2254, CS8602, S2629
