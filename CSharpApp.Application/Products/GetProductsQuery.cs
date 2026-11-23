namespace CSharpApp.Application.Products;

/// <summary>One page of products (ADR-0007, ADR-0010).</summary>
/// <param name="Offset">Items to skip, ≥ 0.</param>
/// <param name="Limit">Page size, 1–100; the endpoint applies the default of 20.</param>
public sealed record GetProductsQuery(int Offset, int Limit) : IQuery<IReadOnlyList<Product>>
{
    /// <summary>Reads the page from the upstream; nothing to add, the query exists for the CQRS seam.</summary>
    public sealed class Handler(IProductsClient products) : IQueryHandler<GetProductsQuery, IReadOnlyList<Product>>
    {
        public ValueTask<IReadOnlyList<Product>> Handle(GetProductsQuery query, CancellationToken cancellationToken) =>
            new(products.GetAllAsync(query.Offset, query.Limit, cancellationToken));
    }
}
