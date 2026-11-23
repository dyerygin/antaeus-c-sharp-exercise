namespace CSharpApp.Core.Interfaces;

/// <summary>Categories of the upstream catalogue.</summary>
/// <remarks>Implemented in Infrastructure as a typed HttpClient with the standard resilience pipeline (ADR-0001, ADR-0004).</remarks>
public interface ICategoriesClient
{
    /// <summary>Up to <paramref name="limit"/> categories.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync(int limit, CancellationToken ct);

    /// <summary>One category by id.</summary>
    Task<Category> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Creates a category; never retried (ADR-0004).</summary>
    Task<Category> CreateAsync(NewCategory category, CancellationToken ct);
}
