using Microsoft.AspNetCore.Mvc;

namespace CSharpApp.Api.Endpoints;

/// <summary>OpenAPI response metadata with a description per status; each endpoint lists only what it can return.</summary>
internal static class EndpointResponses
{
    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [StatusCodes.Status400BadRequest] = "The request is invalid; `errors` names the fields.",
        [StatusCodes.Status401Unauthorized] = "The X-Api-Key header is missing or not a valid key.",
        [StatusCodes.Status404NotFound] = "No resource has this id.",
        [StatusCodes.Status409Conflict] = "A request with this Idempotency-Key is still in progress.",
        [StatusCodes.Status413PayloadTooLarge] = "The request body is larger than the configured limit.",
        [StatusCodes.Status415UnsupportedMediaType] = "The request body is not JSON.",
        [StatusCodes.Status422UnprocessableEntity] = "This Idempotency-Key was already used with a different body.",
        [StatusCodes.Status429TooManyRequests] = "Too many requests; retry later.",
        [StatusCodes.Status502BadGateway] = "The upstream service failed or could not be reached.",
        [StatusCodes.Status504GatewayTimeout] = "The upstream service did not answer in time.",
    };

    extension(RouteHandlerBuilder builder)
    {
        /// <summary>Declares ProblemDetails responses for <paramref name="statuses"/>, each with its description.</summary>
        public RouteHandlerBuilder ProducesProblems(params int[] statuses)
        {
            foreach (var status in statuses)
            {
                builder.WithMetadata(new ProducesResponseTypeAttribute(typeof(ProblemDetails), status, "application/problem+json")
                {
                    Description = Descriptions[status],
                });
            }

            return builder;
        }
    }
}
