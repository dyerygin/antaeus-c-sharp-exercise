namespace CSharpApp.Core.Dtos;

/// <summary>The upstream create-product body; serialized in camelCase.</summary>
public sealed record NewProduct(string Title, decimal Price, string Description, int CategoryId, IReadOnlyList<string> Images);
