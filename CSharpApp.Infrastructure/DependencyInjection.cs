using CSharpApp.Application.Products;

namespace CSharpApp.Infrastructure;

/// <summary>Registers the infrastructure layer: settings validated on start and upstream access.</summary>
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Binds and validates the upstream settings (no <c>BuildServiceProvider</c>, F-11).</summary>
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            services.AddOptions<RestApiSettings>()
                .Bind(configuration.GetSection(RestApiSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddOptions<HttpClientSettings>()
                .Bind(configuration.GetSection(HttpClientSettings.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton<IProductsService, ProductsService>();
            return services;
        }
    }
}
