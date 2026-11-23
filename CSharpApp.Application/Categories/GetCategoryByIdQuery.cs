namespace CSharpApp.Application.Categories;

/// <summary>One category by id; an unknown id surfaces as <c>UpstreamNotFoundException</c> (404, ADR-0005).</summary>
public sealed record GetCategoryByIdQuery(int Id) : IQuery<Category>
{
    /// <summary>Calls the upstream categories client.</summary>
    public sealed class Handler(ICategoriesClient categories) : IQueryHandler<GetCategoryByIdQuery, Category>
    {
        public ValueTask<Category> Handle(GetCategoryByIdQuery query, CancellationToken cancellationToken) =>
            new(categories.GetByIdAsync(query.Id, cancellationToken));
    }
}
