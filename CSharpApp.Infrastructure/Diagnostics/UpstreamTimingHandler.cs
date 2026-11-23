using System.Globalization;

namespace CSharpApp.Infrastructure.Diagnostics;

/// <summary>Times, logs and counts every call of one upstream client (Feature #4, ADR-0015, ASM-13).</summary>
/// <remarks>
/// Registered first, so it is the outermost handler: the time covers retries, the bearer token and the 401 resend, which
/// is what the caller waits for. Logs the path only: query strings can carry ids and headers carry the bearer token.
/// </remarks>
public sealed partial class UpstreamTimingHandler(
    string clientName,
    ILogger<UpstreamTimingHandler> logger,
    UpstreamMetrics metrics,
    TimeProvider time,
    IOptions<HttpClientSettings> settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var start = time.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var elapsed = ElapsedMs(start);
            var status = (int)response.StatusCode;
            var level = elapsed >= settings.Value.SlowThresholdMs ? LogLevel.Warning : LogLevel.Information;
            metrics.RecordRequest(clientName, status.ToString(CultureInfo.InvariantCulture));
            LogCompleted(logger, level, clientName, method, path, status, elapsed);
            return response;
        }
        catch (Exception ex) when (Failed(ex, method, path, start))
        {
            // Never reached: the filter logs and returns false, so the exception keeps its stack and propagates.
            throw;
        }
    }

    private bool Failed(Exception exception, string method, string path, long start)
    {
        var elapsed = ElapsedMs(start);
        metrics.RecordRequest(clientName, "error");
        LogFailed(logger, exception, clientName, method, path, elapsed);
        return false;
    }

    private double ElapsedMs(long start) => Math.Round(time.GetElapsedTime(start).TotalMilliseconds, 1);

    [LoggerMessage(Message = "Upstream {Client} {Method} {Path} -> {StatusCode} in {ElapsedMs} ms")]
    private static partial void LogCompleted(ILogger logger, LogLevel level, string client, string method, string path, int statusCode, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Upstream {Client} {Method} {Path} failed after {ElapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, Exception exception, string client, string method, string path, double elapsedMs);
}
