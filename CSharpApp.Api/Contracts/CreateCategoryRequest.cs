using System.ComponentModel.DataAnnotations;
using CSharpApp.Api.Validation;

namespace CSharpApp.Api.Contracts;

/// <summary>Body of <c>POST /api/v1/categories</c>.</summary>
/// <remarks>Validated by the built-in .NET 10 validation before the handler runs (ADR-0008).</remarks>
public sealed record CreateCategoryRequest
{
    /// <summary>Category name, 1–100 characters.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>An absolute http(s) image URL.</summary>
    [Required, AbsoluteHttpUrl]
    public string Image { get; init; } = string.Empty;
}
