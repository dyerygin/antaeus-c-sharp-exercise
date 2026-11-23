using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.IntegrationTests;

/// <summary>Outside Development only the configured host names are served (F-14).</summary>
public sealed class HostFilteringTests
{
    [Theory]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    [InlineData("localhost", HttpStatusCode.NotFound)]
    public async Task Request_HostHeader_FilteredOutsideDevelopment(string host, HttpStatusCode expected)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("RestApiSettings:Username", "test-user")
            .UseSetting("RestApiSettings:Password", "test-password")
            .UseSetting("ApiAuth:Keys:0:ClientId", "tests")
            .UseSetting("ApiAuth:Keys:0:Key", ApiFactory.TestApiKey));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/no-such-route", UriKind.Relative));
        request.Headers.Host = host;

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }
}
