using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Interfaces;
using CSharpApp.TestData.Builders;
using NSubstitute;

namespace CSharpApp.UnitTests.Categories;

public sealed class CategoryHandlerTests
{
    private readonly ICategoriesClient _categories = Substitute.For<ICategoriesClient>();

    [Fact]
    public async Task GetCategories_PassesLimitAndToken()
    {
        using var cts = new CancellationTokenSource();
        IReadOnlyList<Category> list = [new CategoryBuilder().WithId(2).Build()];
        _categories.GetAllAsync(20, cts.Token).Returns(list);

        var result = await new GetCategoriesQuery.Handler(_categories).Handle(new GetCategoriesQuery(20), cts.Token);

        Assert.Same(list, result);
    }

    [Fact]
    public async Task GetCategoryById_PassesIdAndToken()
    {
        using var cts = new CancellationTokenSource();
        var category = new CategoryBuilder().WithId(4).Build();
        _categories.GetByIdAsync(4, cts.Token).Returns(category);

        var result = await new GetCategoryByIdQuery.Handler(_categories).Handle(new GetCategoryByIdQuery(4), cts.Token);

        Assert.Same(category, result);
    }

    [Fact]
    public async Task CreateCategory_PassesFieldsAndToken_ReturnsUpstreamResult()
    {
        using var cts = new CancellationTokenSource();
        var created = new CategoryBuilder().WithId(6).WithName("Books").Build();
        _categories.CreateAsync(new NewCategory("Books", "https://images.test/books.png"), cts.Token).Returns(created);

        var result = await new CreateCategoryCommand.Handler(_categories).Handle(new CreateCategoryCommand("Books", "https://images.test/books.png"), cts.Token);

        Assert.Same(created, result);
    }
}
