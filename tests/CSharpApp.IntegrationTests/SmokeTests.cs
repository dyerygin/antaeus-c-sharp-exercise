using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Host_StartsInDevelopment_ServesOpenApiDocument()
    {
        // Explicit test credentials: the host validates them on start, and CI has no user-secrets.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("RestApiSettings:Username", "test-user")
            .UseSetting("RestApiSettings:Password", "test-password"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
