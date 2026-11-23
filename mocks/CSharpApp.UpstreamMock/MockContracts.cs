using System.Text.Json.Serialization;

namespace CSharpApp.UpstreamMock;

/// <summary>Wire shapes of the upstream contract subset (ASM-01).</summary>
public static class MockContracts
{
    public sealed record User(int Id, string Email, string Password, string Name, string Role, string Avatar);

    public sealed record Category(int Id, string Name, string Slug, string Image, DateTimeOffset CreationAt, DateTimeOffset UpdatedAt);

    public sealed record Product(
        int Id,
        string Title,
        string Slug,
        decimal Price,
        string Description,
        Category Category,
        IReadOnlyList<string> Images,
        DateTimeOffset CreationAt,
        DateTimeOffset UpdatedAt);

    public sealed record SeedProduct(int Id, string Title, decimal Price, string Description, int CategoryId, IReadOnlyList<string> Images);

    public sealed record Seed(IReadOnlyList<User> Users, IReadOnlyList<Category> Categories, IReadOnlyList<SeedProduct> Products);

    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record CreateProductRequest(string? Title, decimal? Price, string? Description, int? CategoryId, IReadOnlyList<string>? Images);

    public sealed record CreateCategoryRequest(string? Name, string? Image);

    public sealed record TokenPair(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken);
}
