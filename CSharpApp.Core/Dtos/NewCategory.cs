namespace CSharpApp.Core.Dtos;

/// <summary>The upstream create-category body; serialized in camelCase.</summary>
public sealed record NewCategory(string Name, string Image);
