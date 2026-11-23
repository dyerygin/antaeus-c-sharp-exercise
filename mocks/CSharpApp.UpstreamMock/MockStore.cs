using System.Text.Json;
using static CSharpApp.UpstreamMock.MockContracts;

namespace CSharpApp.UpstreamMock;

/// <summary>In-memory users, categories and products, seeded on start and reset on restart.</summary>
public sealed class MockStore
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<User> _users;
    private readonly List<Category> _categories;
    private readonly List<Product> _products;

    public MockStore(TimeProvider time)
    {
        _time = time;
        using var stream = typeof(MockStore).Assembly.GetManifestResourceStream("seed.json")
            ?? throw new InvalidOperationException("The embedded seed.json is missing.");
        var seed = JsonSerializer.Deserialize<Seed>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("seed.json is empty.");
        _users = seed.Users;
        _categories = [.. seed.Categories];
        _products = [.. seed.Products.Select(p => new Product(
            p.Id, p.Title, Slugify(p.Title), p.Price, p.Description, _categories.Single(c => c.Id == p.CategoryId),
            p.Images, _categories[0].CreationAt, _categories[0].CreationAt))];
    }

    public User? FindUser(string? email, string? password) =>
        _users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.Ordinal) && string.Equals(u.Password, password, StringComparison.Ordinal));

    public User? FindUser(int id) => _users.FirstOrDefault(u => u.Id == id);

    public IReadOnlyList<Product> GetProducts(int offset, int? limit)
    {
        lock (_gate)
        {
            var page = _products.Skip(offset);
            return [.. limit is { } take ? page.Take(take) : page];
        }
    }

    public Product? GetProduct(int id)
    {
        lock (_gate)
        {
            return _products.Find(p => p.Id == id);
        }
    }

    public IReadOnlyList<Category> GetCategories(int? limit)
    {
        lock (_gate)
        {
            return [.. limit is { } take ? _categories.Take(take) : _categories];
        }
    }

    public Category? GetCategory(int id)
    {
        lock (_gate)
        {
            return _categories.Find(c => c.Id == id);
        }
    }

    /// <summary>Stores the product and returns it as the original echoes it: with the requested price.</summary>
    public Product AddProduct(string title, decimal price, string description, Category category, IReadOnlyList<string> images)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            var id = _products.Count == 0 ? 1 : _products.Max(p => p.Id) + 1;
            // ASM-05: the original's integer column keeps the integer part, but the create response echoes the input.
            var stored = new Product(id, title, Slugify(title), decimal.Truncate(price), description, category, images, now, now);
            _products.Add(stored);
            return stored with { Price = price };
        }
    }

    public Category AddCategory(string name, string image)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            var category = new Category(_categories.Max(c => c.Id) + 1, name, Slugify(name), image, now, now);
            _categories.Add(category);
            return category;
        }
    }

    private static string Slugify(string text) =>
        string.Join('-', text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
