using CSharpApp.Core.Dtos;

namespace CSharpApp.TestData.Builders;

/// <summary>Readable product test data; defaults are a valid upstream product.</summary>
public sealed class ProductBuilder
{
    private int _id = 1;
    private string _title = "Test product";
    private decimal _price = 10m;
    private Category _category = new CategoryBuilder().Build();
    private IReadOnlyList<string> _images = ["https://images.test/product-1.png"];
    private DateTimeOffset _timestamp = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public ProductBuilder WithId(int id) { _id = id; return this; }

    public ProductBuilder WithTitle(string title) { _title = title; return this; }

    public ProductBuilder WithPrice(decimal price) { _price = price; return this; }

    public ProductBuilder WithCategory(Category category) { _category = category; return this; }

    public ProductBuilder WithImages(params string[] images) { _images = images; return this; }

    public ProductBuilder WithTimestamp(DateTimeOffset timestamp) { _timestamp = timestamp; return this; }

    public Product Build() => new()
    {
        Id = _id,
        Title = _title,
        Price = _price,
        Description = $"{_title} description",
        Category = _category,
        Images = _images,
        CreationAt = _timestamp,
        UpdatedAt = _timestamp,
    };
}
