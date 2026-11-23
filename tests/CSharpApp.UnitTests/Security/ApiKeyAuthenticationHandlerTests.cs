using System.Text.Encodings.Web;
using CSharpApp.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CSharpApp.UnitTests.Security;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private const string DemoKey = "unit-test-api-key-0123456789abcdef";

    private static async Task<AuthenticateResult> AuthenticateAsync(string? presentedKey)
    {
        var schemeOptions = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new ApiKeyAuthenticationOptions());
        var settings = Options.Create(new ApiAuthSettings { Keys = [new ApiClientKey("demo", DemoKey)] });
        var handler = new ApiKeyAuthenticationHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, settings);
        var context = new DefaultHttpContext();
        if (presentedKey is not null)
        {
            context.Request.Headers[ApiKeyAuthenticationOptions.HeaderName] = presentedKey;
        }

        await handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyAuthenticationOptions.Scheme, null, typeof(ApiKeyAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task NoHeader_NoResult()
    {
        var result = await AuthenticateAsync(null);

        Assert.True(result.None);
    }

    [Fact]
    public async Task WrongKey_Fails()
    {
        var result = await AuthenticateAsync("wrong-key-wrong-key-wrong-key-0000");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.DoesNotContain("wrong-key", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidKey_SucceedsWithClientId()
    {
        var result = await AuthenticateAsync(DemoKey);

        Assert.True(result.Succeeded);
        Assert.Equal("demo", result.Principal.FindFirst(ApiKeyAuthenticationOptions.ClientIdClaim)?.Value);
        Assert.Equal(ApiKeyAuthenticationOptions.Scheme, result.Ticket.AuthenticationScheme);
    }

    // R44: a constant-time comparison cannot be timed in a unit test; these pin what it returns.
    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "abcdefghijklmnopqrstuvwxyz012345", true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "abcdefghijklmnopqrstuvwxyz0123456", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "abcdefghijklmnopqrstuvwxyz012346", false)]
    [InlineData("", "abcdefghijklmnopqrstuvwxyz012345", false)]
    public void KeyComparer_EqualOnlyForTheSameKey(string presented, string expected, bool equal) =>
        Assert.Equal(equal, ApiKeyComparer.Matches(presented, expected));
}
