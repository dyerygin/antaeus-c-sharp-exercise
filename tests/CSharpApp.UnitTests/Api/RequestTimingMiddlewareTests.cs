using CSharpApp.Api.Middleware;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CSharpApp.UnitTests.Api;

/// <summary>Feature #4: one inbound timing line per request, outside the error mapping (ADR-0015, R21).</summary>
public sealed class RequestTimingMiddlewareTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<RequestTimingMiddleware> _logger = new();

    private static DefaultHttpContext Get(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Fact]
    public async Task Middleware_LogsElapsed()
    {
        var middleware = new RequestTimingMiddleware(context =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(120));
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }, _logger, _time);

        await middleware.InvokeAsync(Get("/api/v1/products/99", "?secret=value"));

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("HTTP GET /api/v1/products/99 -> 404 in 120 ms", entry.Message);
    }

    [Fact]
    public async Task Middleware_Throws_Logs500AndRethrows()
    {
        var middleware = new RequestTimingMiddleware(_ =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(3));
            throw new InvalidOperationException("boom");
        }, _logger, _time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Get("/api/v1/products")));

        Assert.Equal("HTTP GET /api/v1/products -> 500 in 3 ms", Assert.Single(_logger.Entries).Message);
    }
}
