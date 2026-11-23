using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace CSharpApp.IntegrationTests;

/// <summary>The request log line carries method, path, status and time only: no query string, headers, body or client IP.</summary>
public sealed class RequestLoggingTests
{
    [Fact]
    public async Task Request_IsLogged_WithMethodRouteStatusAndElapsedOnly()
    {
        var sink = new CapturingSink();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("RestApiSettings:Username", "test-user")
            .UseSetting("RestApiSettings:Password", "test-password")
            .ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json?token=secret-value", UriKind.Relative), TestContext.Current.CancellationToken);

        // Feature #4 (ADR-0015): our RequestTimingMiddleware writes the one inbound line per request.
        var request = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal));
        // RequestId and RequestPath come from the ASP.NET Core hosting scope that wraps every line of a request.
        string[] expected = ["ElapsedMs", "EventId", "Method", "Path", "RequestId", "RequestPath", "StatusCode"];
        Assert.Equal(expected, request.Properties.Keys.Where(k => k is not "SourceContext").Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("/openapi/v1.json", Assert.IsType<ScalarValue>(request.Properties["Path"]).Value);
        Assert.DoesNotContain("secret-value", request.RenderMessage(), StringComparison.Ordinal);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IEnumerable<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
