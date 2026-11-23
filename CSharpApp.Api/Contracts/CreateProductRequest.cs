using System.ComponentModel.DataAnnotations;
using CSharpApp.Api.Validation;

namespace CSharpApp.Api.Contracts;

/// <summary>Body of <c>POST /api/v1/products</c>.</summary>
/// <remarks>Validated by the built-in .NET 10 validation before the handler runs (ADR-0008); unknown members, duplicate
/// members and <c>null</c> in non-nullable members are rejected by the JSON reader (ADR-0006).</remarks>
public sealed record CreateProductRequest
{
    /// <summary>Product title, 1–200 characters.</summary>
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    /// <summary>Price, 0.01–1 000 000. The upstream stores whole numbers (ASM-05); the response shows the stored value.</summary>
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Price { get; init; }

    /// <summary>Description, 1–2000 characters.</summary>
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Id of an existing category, 1 or more; an unknown id returns 400 on this field.</summary>
    [Range(1, int.MaxValue)]
    public int CategoryId { get; init; }

    /// <summary>1–10 absolute http(s) image URLs.</summary>
    [Required, MinLength(1), MaxLength(10), AbsoluteHttpUrls]
    public IReadOnlyList<string> Images { get; init; } = [];
}
