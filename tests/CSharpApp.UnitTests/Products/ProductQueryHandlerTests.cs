using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Interfaces;
using CSharpApp.TestData.Builders;
using NSubstitute;

namespace CSharpApp.UnitTests.Products;

public sealed class ProductQueryHandlerTests
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    [Fact]
    public async Task GetProducts_PassesOffsetLimitAndToken()
    {
        using var cts = new CancellationTokenSource();
        IReadOnlyList<Product> page = [new ProductBuilder().WithId(3).Build()];
        _products.GetAllAsync(40, 20, cts.Token).Returns(page);

        var result = await new GetProductsQuery.Handler(_products).Handle(new GetProductsQuery(40, 20), cts.Token);

        Assert.Same(page, result);
    }

    [Fact]
    public async Task GetProductById_PassesIdAndToken()
    {
        using var cts = new CancellationTokenSource();
        var product = new ProductBuilder().WithId(7).Build();
        _products.GetByIdAsync(7, cts.Token).Returns(product);

        var result = await new GetProductByIdQuery.Handler(_products).Handle(new GetProductByIdQuery(7), cts.Token);

        Assert.Same(product, result);
    }
}
