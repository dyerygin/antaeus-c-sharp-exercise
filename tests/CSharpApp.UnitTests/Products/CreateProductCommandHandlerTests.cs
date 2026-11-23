using CSharpApp.Application.Common;
using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Exceptions;
using CSharpApp.Core.Interfaces;
using CSharpApp.Core.Settings;
using CSharpApp.TestData.Builders;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CSharpApp.UnitTests.Products;

/// <summary>Create, then read back the stored product (ADR-0011, ASM-05, ASM-10).</summary>
public sealed class CreateProductCommandHandlerTests
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly ListLogger<CreateProductCommand.Handler> _logger = new();

    private static CreateProductCommand Command { get; } = new("Lamp", 10.5m, "A lamp", 3, ["https://images.test/lamp.png"]);

    private CreateProductCommand.Handler CreateHandler() =>
        new(_products, Options.Create(new HttpClientSettings { TotalTimeoutSeconds = 5, AttemptTimeoutSeconds = 1 }), _logger);

    [Fact]
    public async Task CreateProduct_PassesFieldsAndToken()
    {
        using var cts = new CancellationTokenSource();
        _products.CreateAsync(Arg.Any<NewProduct>(), cts.Token).Returns(new ProductBuilder().WithId(9).Build());
        _products.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(new ProductBuilder().WithId(9).Build());

        await CreateHandler().Handle(Command, cts.Token);

        await _products.Received(1).CreateAsync(new NewProduct("Lamp", 10.5m, "A lamp", 3, Command.Images), cts.Token);
    }

    [Fact]
    public async Task CreateProduct_FractionalPrice_ReturnsStoredPrice()
    {
        _products.CreateAsync(Arg.Any<NewProduct>(), Arg.Any<CancellationToken>()).Returns(new ProductBuilder().WithId(9).WithPrice(10.5m).Build());
        _products.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(new ProductBuilder().WithId(9).WithPrice(10m).Build());

        var product = await CreateHandler().Handle(Command, CancellationToken.None);

        Assert.Equal(10m, product.Price);
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_ThrowsRequestValidationException()
    {
        _products.CreateAsync(Arg.Any<NewProduct>(), Arg.Any<CancellationToken>()).ThrowsAsync(new UpstreamNotFoundException("no such entity"));

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => CreateHandler().Handle(Command, CancellationToken.None).AsTask());

        Assert.True(error.Errors.ContainsKey("categoryId"));
        await _products.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ReadBackFailures))]
    public async Task CreateProduct_ReadBackFails_ReturnsEchoAndLogsWarning(Exception failure)
    {
        var echo = new ProductBuilder().WithId(9).WithPrice(10.5m).Build();
        _products.CreateAsync(Arg.Any<NewProduct>(), Arg.Any<CancellationToken>()).Returns(echo);
        _products.GetByIdAsync(9, Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var product = await CreateHandler().Handle(Command, CancellationToken.None);

        Assert.Same(echo, product);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    public static TheoryData<Exception> ReadBackFailures() =>
    [
        new UpstreamUnavailableException("down"),
        new UpstreamTimeoutException("slow"),
        new UpstreamNotFoundException("not yet visible"),
        new OperationCanceledException(),
    ];

    [Fact]
    public async Task ReadBack_UsesOwnTimeout_NotRequestToken()
    {
        // W11/R29: the caller goes away right after the upstream create; the read-back still runs to completion.
        using var request = new CancellationTokenSource();
        _products.CreateAsync(Arg.Any<NewProduct>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            request.Cancel();
            return new ProductBuilder().WithId(9).WithPrice(10.5m).Build();
        });
        _products.GetByIdAsync(9, Arg.Is<CancellationToken>(t => !t.IsCancellationRequested))
            .Returns(new ProductBuilder().WithId(9).WithPrice(10m).Build());

        var product = await CreateHandler().Handle(Command, request.Token);

        Assert.Equal(10m, product.Price);
    }
}
