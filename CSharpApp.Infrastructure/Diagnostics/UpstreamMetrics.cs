using System.Diagnostics.Metrics;

namespace CSharpApp.Infrastructure.Diagnostics;

/// <summary>Upstream call counters on the <c>CSharpApp.Upstream</c> meter (Feature #4, ADR-0015).</summary>
/// <remarks>Read them with <c>dotnet-counters monitor --counters CSharpApp.Upstream</c> or any OpenTelemetry exporter.</remarks>
public sealed class UpstreamMetrics(IMeterFactory meterFactory)
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "CSharpApp.Upstream";

    private readonly Counter<long> _requests = meterFactory.Create(MeterName)
        .CreateCounter<long>("csharpapp.upstream.requests", unit: "{request}", description: "Upstream calls by client and status code");

    /// <summary>Counts one upstream call; <paramref name="status"/> is the status code, or <c>error</c> when none came back.</summary>
    public void RecordRequest(string client, string status) =>
        _requests.Add(1, new KeyValuePair<string, object?>("client", client), new KeyValuePair<string, object?>("status", status));
}
