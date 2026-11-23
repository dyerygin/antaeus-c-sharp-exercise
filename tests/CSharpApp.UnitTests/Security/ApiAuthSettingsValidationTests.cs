using CSharpApp.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Security;

/// <summary>R8, T9: API keys are validated on start, item by item, with an actionable message.</summary>
public sealed class ApiAuthSettingsValidationTests
{
    private static ApiAuthSettings Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddApiKeyAuthentication(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ApiAuthSettings>>().Value;
    }

    [Fact]
    public void ValidKey_Binds()
    {
        var settings = Resolve(new(StringComparer.Ordinal)
        {
            ["ApiAuth:Keys:0:ClientId"] = "demo",
            ["ApiAuth:Keys:0:Key"] = new string('k', 32),
        });

        var key = Assert.Single(settings.Keys);
        Assert.Equal("demo", key.ClientId);
    }

    [Fact]
    public void NoKeys_FailsWithHint()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new(StringComparer.Ordinal)));

        Assert.Contains("ApiAuth:Keys is empty", error.Message, StringComparison.Ordinal);
        Assert.Contains("API_KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Key31Characters_Fails()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new(StringComparer.Ordinal)
        {
            ["ApiAuth:Keys:0:ClientId"] = "demo",
            ["ApiAuth:Keys:0:Key"] = new string('k', 31),
        }));

        Assert.Contains("ApiAuth:Keys:0", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('k', 31), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingClientId_Fails()
    {
        Assert.Throws<OptionsValidationException>(() => Resolve(new(StringComparer.Ordinal)
        {
            ["ApiAuth:Keys:0:Key"] = new string('k', 32),
        }));
    }
}
