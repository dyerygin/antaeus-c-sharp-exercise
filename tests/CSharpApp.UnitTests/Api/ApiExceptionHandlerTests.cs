using System.Text.Json;
using CSharpApp.Api.ErrorHandling;
using CSharpApp.Application.Common;
using CSharpApp.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CSharpApp.UnitTests.Api;

/// <summary>Exception → ProblemDetails status, title and body (ADR-0005).</summary>
public sealed class ApiExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(int Status, JsonElement? Body)> HandleAsync(Exception exception, bool clientAborted = false)
    {
        await using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        if (clientAborted)
        {
            context.RequestAborted = new CancellationToken(canceled: true);
        }

        var handler = new ApiExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, Ct));

        context.Response.Body.Position = 0;
        if (context.Response.Body.Length == 0)
        {
            return (context.Response.StatusCode, null);
        }

        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: Ct);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task RequestValidation_Writes400ValidationProblem()
    {
        var (status, body) = await HandleAsync(new RequestValidationException("categoryId", "The category does not exist."));

        Assert.Equal(400, status);
        Assert.Equal("The category does not exist.", body!.Value.GetProperty("errors").GetProperty("categoryId")[0].GetString());
    }

    [Fact]
    public async Task NotFound_Writes404Problem()
    {
        var (status, body) = await HandleAsync(new UpstreamNotFoundException("The upstream service has no such entity."));

        Assert.Equal(404, status);
        Assert.Equal(404, body!.Value.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.Value.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UpstreamBadRequest_Writes400ProblemWithoutUpstreamMessage()
    {
        var (status, body) = await HandleAsync(new UpstreamBadRequestException("internal upstream detail"));

        Assert.Equal(400, status);
        Assert.DoesNotContain("internal upstream detail", body!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_Writes502Problem()
    {
        var (status, _) = await HandleAsync(new UpstreamUnavailableException("The upstream service answered 503."));

        Assert.Equal(502, status);
    }

    [Fact]
    public async Task ContractError_Writes502Problem()
    {
        var (status, _) = await HandleAsync(new UpstreamContractException("bad body", new JsonException("at line 1")));

        Assert.Equal(502, status);
    }

    [Fact]
    public async Task BrokenCircuit_Writes502Problem()
    {
        var (status, _) = await HandleAsync(new BrokenCircuitException());

        Assert.Equal(502, status);
    }

    [Fact]
    public async Task Timeout_Writes504Problem()
    {
        var (status, body) = await HandleAsync(new TimeoutRejectedException());

        Assert.Equal(504, status);
        Assert.Equal(504, body!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ClientAborted_Writes499WithoutBody()
    {
        var (status, body) = await HandleAsync(new OperationCanceledException(), clientAborted: true);

        Assert.Equal(499, status);
        Assert.Null(body);
    }

    [Fact]
    public async Task BadHttpRequest_WritesItsStatusCode()
    {
        var (status, _) = await HandleAsync(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(413, status);
    }

    [Fact]
    public async Task Unknown_Writes500WithoutExceptionMessage()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("secret connection string"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", body!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(body.Value.GetProperty("traceId").GetString()));
    }
}
