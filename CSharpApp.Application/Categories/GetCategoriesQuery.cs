namespace CSharpApp.Application.Categories;

/// <summary>Up to <c>Limit</c> categories (default 20, maximum 100; ADR-0010).</summary>
public sealed record GetCategoriesQuery(int Limit) : IQuery<IReadOnlyList<Category>>
{
    /// <summary>Calls the upstream categories client.</summary>
    public sealed class Handler(ICategoriesClient categories) : IQueryHandler<GetCategoriesQuery, IReadOnlyList<Category>>
    {
        public ValueTask<IReadOnlyList<Category>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken) =>
            new(categories.GetAllAsync(query.Limit, cancellationToken));
    }
}
