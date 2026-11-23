namespace CSharpApp.Core.Dtos;

/// <summary>A product as the upstream catalogue returns it; also the API response (ADR-0009).</summary>
/// <remarks>
/// <c>Price</c> is decimal because create echoes fractional prices (F-02, ASM-05); <c>Images</c> is settable so it is
/// deserialized (F-06); timestamps keep their offset (DateTimeOffset). Members the upstream may omit stay nullable.
/// </remarks>
public sealed record Product
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public decimal Price { get; init; }

    public string? Description { get; init; }

    public Category? Category { get; init; }

    public IReadOnlyList<string> Images { get; init; } = [];

    public DateTimeOffset? CreationAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
