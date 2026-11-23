using CSharpApp.Application.Common;
using CSharpApp.Core.Exceptions;

namespace CSharpApp.Application.Products;

/// <summary>Creates a product upstream and returns it as stored (ADR-0011).</summary>
public sealed partial record CreateProductCommand(string Title, decimal Price, string Description, int CategoryId, IReadOnlyList<string> Images)
    : ICommand<Product>
{
    /// <summary>Create, then read back the stored product.</summary>
    public sealed partial class Handler(IProductsClient products, IOptions<HttpClientSettings> http, ILogger<Handler> logger)
        : ICommandHandler<CreateProductCommand, Product>
    {
        public async ValueTask<Product> Handle(CreateProductCommand command, CancellationToken cancellationToken)
        {
            Product created;
            try
            {
                created = await products.CreateAsync(
                    new NewProduct(command.Title, command.Price, command.Description, command.CategoryId, command.Images), cancellationToken);
            }
            catch (UpstreamNotFoundException)
            {
                // ASM-10: the upstream reports an unknown categoryId as EntityNotFoundError; for the caller it is a field error.
                throw new RequestValidationException("categoryId", "The category does not exist.");
            }

            // ADR-0011, ASM-05: the upstream echoes a fractional price but stores a whole number; return what is stored.
            // The create has happened, so the read-back runs on its own timeout, not the caller's token.
            using var readBack = new CancellationTokenSource(TimeSpan.FromSeconds(http.Value.TotalTimeoutSeconds));
            try
            {
                return await products.GetByIdAsync(created.Id, readBack.Token);
            }
            catch (Exception ex) when (ex is UpstreamException or OperationCanceledException)
            {
                // The product exists; failing the request now would invite a duplicate create on retry.
                LogReadBackFailed(logger, ex, created.Id);
                return created;
            }
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Product {ProductId} was created but could not be read back; returning the create echo")]
        private static partial void LogReadBackFailed(ILogger logger, Exception exception, int productId);
    }
}
