namespace CSharpApp.Core.Interfaces;

/// <summary>Products of the upstream catalogue.</summary>
/// <remarks>Implemented in Infrastructure as a typed HttpClient with the standard resilience pipeline (ADR-0001, ADR-0004).</remarks>
public interface IProductsClient
{
    /// <summary>One page of products.</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(int offset, int limit, CancellationToken ct);

    /// <summary>One product by id.</summary>
    Task<Product> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Creates a product; never retried (ADR-0004).</summary>
    Task<Product> CreateAsync(NewProduct product, CancellationToken ct);
}
