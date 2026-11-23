using System.ComponentModel.DataAnnotations;
using CSharpApp.Api.Contracts;
using CSharpApp.Api.RateLimiting;
using CSharpApp.Api.Security;
using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Endpoints;

/// <summary>The products resource: list, get by id and create (ADR-0007, ADR-0010, ADR-0011).</summary>
public static class ProductsEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>Maps <c>/api/v1/products</c>.</summary>
        public IEndpointRouteBuilder MapProductsEndpoints()
        {
            var products = app.NewVersionedApi("Products")
                .MapGroup("api/v{version:apiVersion}/products")
                .HasApiVersion(1.0)
                .RequireRateLimiting(ApiRateLimiting.Policy);

            products.MapGet("", GetProductsAsync)
                .WithName("GetProducts")
                .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests, StatusCodes.Status502BadGateway, StatusCodes.Status504GatewayTimeout);

            products.MapGet("{id:int}", GetProductAsync)
                .WithName("GetProduct")
                .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status429TooManyRequests, StatusCodes.Status502BadGateway, StatusCodes.Status504GatewayTimeout);

            products.MapPost("", CreateProductAsync)
                .WithName("CreateProduct")
                .RequireAuthorization(ApiKeyAuthentication.WritePolicy)
                .ProducesProblems(
                    StatusCodes.Status400BadRequest,
                    StatusCodes.Status401Unauthorized,
                    StatusCodes.Status413PayloadTooLarge,
                    StatusCodes.Status415UnsupportedMediaType,
                    StatusCodes.Status429TooManyRequests,
                    StatusCodes.Status502BadGateway,
                    StatusCodes.Status504GatewayTimeout);

            return app;
        }
    }

    /// <summary>Lists products, one page at a time.</summary>
    /// <remarks>Without <c>limit</c> the page size is <c>Paging:DefaultLimit</c> (20); the maximum is 100 (ADR-0010).</remarks>
    /// <param name="offset">Products to skip; 0 or more, default 0.</param>
    /// <param name="limit">Page size, 1 to 100.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="paging">Paging defaults.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<Ok<IReadOnlyList<Product>>> GetProductsAsync(
        [Range(0, int.MaxValue)] int? offset,
        [Range(1, 100)] int? limit,
        ISender sender,
        IOptions<PagingSettings> paging,
        CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(new GetProductsQuery(offset ?? 0, limit ?? paging.Value.DefaultLimit), ct));

    /// <summary>Gets one product by id.</summary>
    /// <param name="id">Product id, 1 or more.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<Ok<Product>> GetProductAsync([Range(1, int.MaxValue)] int id, ISender sender, CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(new GetProductByIdQuery(id), ct));

    /// <summary>Creates a product and returns it as the upstream stored it.</summary>
    /// <remarks>
    /// The upstream rounds prices down to whole numbers (ASM-05), so the response is read back after the create
    /// (ADR-0011). An unknown <c>categoryId</c> returns 400 on that field. A create is never retried (ADR-0004).
    /// </remarks>
    /// <param name="request">The new product.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<CreatedAtRoute<Product>> CreateProductAsync(CreateProductRequest request, ISender sender, CancellationToken ct)
    {
        var product = await sender.Send(
            new CreateProductCommand(request.Title, request.Price, request.Description, request.CategoryId, request.Images), ct);
        return TypedResults.CreatedAtRoute(product, "GetProduct", new RouteValueDictionary { ["id"] = product.Id, ["version"] = "1" });
    }
}
