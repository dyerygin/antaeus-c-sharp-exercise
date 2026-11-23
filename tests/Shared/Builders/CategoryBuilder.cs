using CSharpApp.Core.Dtos;

namespace CSharpApp.TestData.Builders;

/// <summary>Readable category test data; defaults are a valid upstream category.</summary>
public sealed class CategoryBuilder
{
    private int _id = 1;
    private string _name = "Clothes";

    public CategoryBuilder WithId(int id) { _id = id; return this; }

    public CategoryBuilder WithName(string name) { _name = name; return this; }

    public Category Build() => new()
    {
        Id = _id,
        Name = _name,
        Slug = _name.ToLowerInvariant(),
        Image = $"https://images.test/{_name.ToLowerInvariant()}.png",
        CreationAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
    };
}
