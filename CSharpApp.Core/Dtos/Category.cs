namespace CSharpApp.Core.Dtos;

/// <summary>A product category as the upstream catalogue returns it; also the API response (ADR-0009).</summary>
public sealed record Category
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public string? Slug { get; init; }

    public string? Image { get; init; }

    public DateTimeOffset? CreationAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
