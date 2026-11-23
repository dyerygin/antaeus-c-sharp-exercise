using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Diagnostics;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSharpApp.UnitTests.Diagnostics;

/// <summary>Feature #4: every upstream call is timed, logged and counted (ADR-0015, ASM-13).</summary>
public sealed class UpstreamTimingHandlerTests : IDisposable
{
    private static readonly Uri ProductsUri = new("http://upstream.test/api/v1/products?offset=0&limit=20&token=secret-value");

    private readonly FakeTimeProvider _time = new();
    private readonly ListLogger<UpstreamTimingHandler> _logger = new();
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _services.Dispose();

    private HttpClient CreateClient(HttpMessageHandler upstream) =>
        new(new UpstreamTimingHandler(
            "products",
            _logger,
            new UpstreamMetrics(_services.GetRequiredService<IMeterFactory>()),
            _time,
            Options.Create(new HttpClientSettings { SlowThresholdMs = 1000 }))
        {
            InnerHandler = upstream,
        });

    private StubHttpMessageHandler Answers(HttpStatusCode status, TimeSpan takes) => new((_, _) =>
    {
        _time.Advance(takes);
        return Task.FromResult(new HttpResponseMessage(status));
    });

    [Fact]
    public async Task LogsClientMethodPathStatusElapsed()
    {
        using var client = CreateClient(Answers(HttpStatusCode.OK, TimeSpan.FromMilliseconds(250)));

        using var response = await client.GetAsync(ProductsUri, Ct);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Upstream products GET /api/v1/products -> 200 in 250 ms", entry.Message);
    }

    [Fact]
    public async Task SlowResponse_LogsWarning()
    {
        using var client = CreateClient(Answers(HttpStatusCode.OK, TimeSpan.FromMilliseconds(1500)));

        using var response = await client.GetAsync(ProductsUri, Ct);

        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task Throws_LogsErrorAndRethrows()
    {
        using var client = CreateClient(new StubHttpMessageHandler((_, _) =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(40));
            throw new HttpRequestException("Connection refused");
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(ProductsUri, Ct));

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Upstream products GET /api/v1/products failed after 40 ms", entry.Message);
    }

    [Fact]
    public async Task DoesNotLogQueryOrAuthorization()
    {
        using var client = CreateClient(Answers(HttpStatusCode.OK, TimeSpan.FromMilliseconds(5)));
        using var request = new HttpRequestMessage(HttpMethod.Get, ProductsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token");

        using var response = await client.SendAsync(request, Ct);

        var message = Assert.Single(_logger.Entries).Message;
        Assert.DoesNotContain("secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("limit", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_IncrementsRequestCounter()
    {
        var measurements = new List<(long Value, string? Client, string? Status)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, UpstreamMetrics.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Name, "csharpapp.upstream.requests", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var all = tags.ToArray();
            string? Tag(string key) => all.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal)).Value?.ToString();
            measurements.Add((value, Tag("client"), Tag("status")));
        });
        listener.Start();
        using var client = CreateClient(Answers(HttpStatusCode.NotFound, TimeSpan.FromMilliseconds(5)));

        using var response = await client.GetAsync(ProductsUri, Ct);

        Assert.Equal((1L, "products", "404"), Assert.Single(measurements));
    }
}
