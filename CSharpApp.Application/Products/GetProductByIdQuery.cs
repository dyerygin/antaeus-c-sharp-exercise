namespace CSharpApp.Application.Products;

/// <summary>One product by id; an unknown id surfaces as <c>UpstreamNotFoundException</c> (404, ADR-0005).</summary>
public sealed record GetProductByIdQuery(int Id) : IQuery<Product>
{
    /// <summary>Reads the product from the upstream.</summary>
    public sealed class Handler(IProductsClient products) : IQueryHandler<GetProductByIdQuery, Product>
    {
        public ValueTask<Product> Handle(GetProductByIdQuery query, CancellationToken cancellationToken) =>
            new(products.GetByIdAsync(query.Id, cancellationToken));
    }
}
