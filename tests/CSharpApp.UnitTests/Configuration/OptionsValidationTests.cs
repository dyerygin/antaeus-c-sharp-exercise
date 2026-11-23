using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Configuration;

public sealed class OptionsValidationTests
{
    private static Dictionary<string, string?> ValidSettings() => new(StringComparer.Ordinal)
    {
        ["RestApiSettings:BaseUrl"] = "http://upstream.test/api/v1/",
        ["RestApiSettings:Products"] = " products ",
        ["RestApiSettings:Categories"] = "categories",
        ["RestApiSettings:Auth"] = "auth/login",
        ["RestApiSettings:RefreshToken"] = "auth/refresh-token",
        ["RestApiSettings:Profile"] = "auth/profile",
        ["RestApiSettings:Username"] = "user@test",
        ["RestApiSettings:Password"] = "test-password",
        ["HttpClientSettings:TotalTimeoutSeconds"] = "10",
        ["HttpClientSettings:AttemptTimeoutSeconds"] = "3",
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    [Fact]
    public void ValidConfig_Binds()
    {
        using var provider = BuildProvider(ValidSettings());

        var restApi = provider.GetRequiredService<IOptions<RestApiSettings>>().Value;
        var http = provider.GetRequiredService<IOptions<HttpClientSettings>>().Value;

        Assert.Equal("http://upstream.test/api/v1/", restApi.BaseUrl);
        Assert.Equal("products", restApi.Products);
        Assert.Equal(10, http.TotalTimeoutSeconds);
        Assert.Equal(3, http.AttemptTimeoutSeconds);
        Assert.Equal(2, http.RetryCount);
    }

    [Fact]
    public void MissingBaseUrl_FailsOnResolve()
    {
        var settings = ValidSettings();
        settings.Remove("RestApiSettings:BaseUrl");
        using var provider = BuildProvider(settings);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RestApiSettings>>().Value);

        Assert.Contains("BaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrlWithoutTrailingSlash_FailsValidation()
    {
        var settings = ValidSettings();
        settings["RestApiSettings:BaseUrl"] = "http://upstream.test/api/v1";
        using var provider = BuildProvider(settings);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RestApiSettings>>().Value);

        Assert.Contains("must end with '/'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RestApiSettings:Products")]
    [InlineData("RestApiSettings:Categories")]
    [InlineData("RestApiSettings:Auth")]
    public void LeadingSlashPath_FailsValidation(string key)
    {
        var settings = ValidSettings();
        settings[key] = "/" + settings[key]!.Trim();
        using var provider = BuildProvider(settings);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RestApiSettings>>().Value);

        Assert.Contains("no leading '/'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCredentials_FailsWithActionableMessage()
    {
        var settings = ValidSettings();
        settings.Remove("RestApiSettings:Username");
        using var provider = BuildProvider(settings);

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RestApiSettings>>().Value);

        Assert.Contains("RestApiSettings:Username is empty", error.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", error.Message, StringComparison.Ordinal);
        Assert.Contains(".env.example", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptTimeoutOutOfRange_FailsValidation()
    {
        var settings = ValidSettings();
        settings["HttpClientSettings:AttemptTimeoutSeconds"] = "0";
        using var provider = BuildProvider(settings);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HttpClientSettings>>().Value);
    }
}
