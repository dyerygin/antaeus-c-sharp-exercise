using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace CSharpApp.IntegrationTests;

/// <summary>T8: request log events carry no query string, headers, body or client IP.</summary>
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

        var request = Assert.Single(sink.Events, e => e.Properties.ContainsKey("RoutePattern"));
        string[] expected = ["Elapsed", "RequestMethod", "RoutePattern", "StatusCode"];
        Assert.Equal(expected, request.Properties.Keys.Where(k => k is not "SourceContext").Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("/openapi/{documentName}.json", Assert.IsType<ScalarValue>(request.Properties["RoutePattern"]).Value);
        Assert.DoesNotContain("secret-value", request.RenderMessage(), StringComparison.Ordinal);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IEnumerable<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
