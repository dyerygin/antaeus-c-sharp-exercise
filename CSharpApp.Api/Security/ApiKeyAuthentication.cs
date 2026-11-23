namespace CSharpApp.Api.Security;

/// <summary>Registers API-key authentication and the <see cref="WritePolicy"/> the POST endpoints require (ADR-0012).</summary>
public static class ApiKeyAuthentication
{
    /// <summary>The policy of the write endpoints: an authenticated API client.</summary>
    public const string WritePolicy = "WriteAccess";

    extension(IServiceCollection services)
    {
        /// <summary>Adds the validated key settings, the <c>ApiKey</c> scheme and the write policy.</summary>
        public IServiceCollection AddApiKeyAuthentication(IConfiguration configuration)
        {
            services.AddOptions<ApiAuthSettings>()
                .Bind(configuration.GetSection(ApiAuthSettings.SectionName))
                .ValidateOnStart();
            services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ApiAuthSettings>, ApiAuthSettingsValidator>();

            services.AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
                .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.Scheme, null);
            services.AddAuthorizationBuilder()
                .AddPolicy(WritePolicy, policy => policy
                    .AddAuthenticationSchemes(ApiKeyAuthenticationOptions.Scheme)
                    .RequireAuthenticatedUser());
            return services;
        }
    }
}
