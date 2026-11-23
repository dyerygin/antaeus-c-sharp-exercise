using CSharpApp.Api.Endpoints;

namespace CSharpApp.Api.Idempotency;

/// <summary>Opts a create endpoint into <c>Idempotency-Key</c> handling (ADR-0013).</summary>
public static class IdempotentEndpoints
{
    extension(RouteHandlerBuilder builder)
    {
        /// <summary>Adds the filter, the OpenAPI header and the 409/422 responses.</summary>
        public RouteHandlerBuilder WithIdempotency<TRequest>()
            where TRequest : class =>
            builder
                .AddEndpointFilter<IdempotencyFilter<TRequest>>()
                .WithMetadata(new IdempotentEndpointMetadata())
                .ProducesProblems(StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);
    }
}
