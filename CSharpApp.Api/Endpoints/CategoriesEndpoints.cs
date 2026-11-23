using System.ComponentModel.DataAnnotations;
using CSharpApp.Api.Contracts;
using CSharpApp.Api.RateLimiting;
using CSharpApp.Api.Security;
using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Endpoints;

/// <summary>The categories resource: list, get by id and create (ADR-0007, ADR-0010).</summary>
public static class CategoriesEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        /// <summary>Maps <c>/api/v1/categories</c>.</summary>
        public IEndpointRouteBuilder MapCategoriesEndpoints()
        {
            var categories = app.NewVersionedApi("Categories")
                .MapGroup("api/v{version:apiVersion}/categories")
                .HasApiVersion(1.0)
                .RequireRateLimiting(ApiRateLimiting.Policy);

            categories.MapGet("", GetCategoriesAsync)
                .WithName("GetCategories")
                .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests, StatusCodes.Status502BadGateway, StatusCodes.Status504GatewayTimeout);

            categories.MapGet("{id:int}", GetCategoryAsync)
                .WithName("GetCategory")
                .ProducesProblems(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status429TooManyRequests, StatusCodes.Status502BadGateway, StatusCodes.Status504GatewayTimeout);

            categories.MapPost("", CreateCategoryAsync)
                .WithName("CreateCategory")
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

    /// <summary>Lists categories.</summary>
    /// <remarks>The upstream has no offset for categories; without <c>limit</c> the page size is 20, at most 100 (ADR-0010).</remarks>
    /// <param name="limit">Number of categories, 1 to 100.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="paging">Paging defaults.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<Ok<IReadOnlyList<Category>>> GetCategoriesAsync(
        [Range(1, 100)] int? limit,
        ISender sender,
        IOptions<PagingSettings> paging,
        CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(new GetCategoriesQuery(limit ?? paging.Value.DefaultLimit), ct));

    /// <summary>Gets one category by id.</summary>
    /// <param name="id">Category id, 1 or more.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<Ok<Category>> GetCategoryAsync([Range(1, int.MaxValue)] int id, ISender sender, CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(new GetCategoryByIdQuery(id), ct));

    /// <summary>Creates a category.</summary>
    /// <remarks>The upstream stores category fields as sent, so the create response is returned as is (ASM-11). Never retried (ADR-0004).</remarks>
    /// <param name="request">The new category.</param>
    /// <param name="sender">The mediator.</param>
    /// <param name="ct">Aborted when the caller goes away.</param>
    internal static async Task<CreatedAtRoute<Category>> CreateCategoryAsync(CreateCategoryRequest request, ISender sender, CancellationToken ct)
    {
        var category = await sender.Send(new CreateCategoryCommand(request.Name, request.Image), ct);
        return TypedResults.CreatedAtRoute(category, "GetCategory", new RouteValueDictionary { ["id"] = category.Id, ["version"] = "1" });
    }
}
