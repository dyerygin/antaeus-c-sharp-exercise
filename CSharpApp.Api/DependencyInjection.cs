using CSharpApp.Api.ErrorHandling;

namespace CSharpApp.Api;

/// <summary>Registers the API layer: error mapping to ProblemDetails (ADR-0005).</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds ProblemDetails and the exception handler that maps upstream failures.</summary>
        public IServiceCollection AddApi()
        {
            services.AddProblemDetails();
            services.AddExceptionHandler<ApiExceptionHandler>();
            return services;
        }
    }
}
