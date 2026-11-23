using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Host_StartsInDevelopment_ServesOpenApiDocument()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
