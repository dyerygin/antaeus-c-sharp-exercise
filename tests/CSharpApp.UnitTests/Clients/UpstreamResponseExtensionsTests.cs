using System.Net;
using System.Text;
using CSharpApp.Core.Exceptions;
using CSharpApp.Infrastructure.Clients;

namespace CSharpApp.UnitTests.Clients;

/// <summary>Upstream status and body → exception kind (ADR-0005, ASM-10).</summary>
public sealed class UpstreamResponseExtensionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "") => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Upstream404_MapsToNotFound()
    {
        using var response = Response(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<UpstreamNotFoundException>(() => response.EnsureUpstreamSuccessAsync(Ct));
    }

    [Fact]
    public async Task Upstream400EntityNotFound_MapsToNotFound()
    {
        using var response = Response(HttpStatusCode.BadRequest,
            """{"path":"/api/v1/products/999","timestamp":"2026-11-23T10:00:00.000Z","name":"EntityNotFoundError","message":"Could not find any entity of type \"Product\""}""");

        await Assert.ThrowsAsync<UpstreamNotFoundException>(() => response.EnsureUpstreamSuccessAsync(Ct));
    }

    [Fact]
    public async Task Upstream400_MapsToBadRequest()
    {
        using var response = Response(HttpStatusCode.BadRequest,
            """{"message":["price must be a positive number","images must contain at least 1 elements"],"error":"Bad Request","statusCode":400}""");

        var error = await Assert.ThrowsAsync<UpstreamBadRequestException>(() => response.EnsureUpstreamSuccessAsync(Ct));

        Assert.Equal("price must be a positive number; images must contain at least 1 elements", error.UpstreamMessage);
        Assert.DoesNotContain("price", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"message":"Validation failed (numeric string is expected)"}""", "Validation failed (numeric string is expected)")]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public async Task Upstream422OrUnreadable400_MapsToBadRequest(string body, string? expectedMessage)
    {
        using var response = Response(body.Length == 0 ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadRequest, body);

        var error = await Assert.ThrowsAsync<UpstreamBadRequestException>(() => response.EnsureUpstreamSuccessAsync(Ct));

        Assert.Equal(expectedMessage, error.UpstreamMessage);
    }

    [Fact]
    public async Task Upstream503_MapsToUnavailable()
    {
        using var response = Response(HttpStatusCode.ServiceUnavailable, """{"message":"down"}""");

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => response.EnsureUpstreamSuccessAsync(Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task UpstreamCredentialsRedirectOrServerError_MapsToUnavailable(HttpStatusCode status)
    {
        using var response = Response(status);

        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => response.EnsureUpstreamSuccessAsync(Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public async Task Success_DoesNotThrow(HttpStatusCode status)
    {
        using var response = Response(status, "{}");

        var error = await Record.ExceptionAsync(() => response.EnsureUpstreamSuccessAsync(Ct));

        Assert.Null(error);
    }
}
