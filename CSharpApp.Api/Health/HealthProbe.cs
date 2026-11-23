using System.Globalization;
using System.Net;

namespace CSharpApp.Api.Health;

/// <summary>
/// The container health check: <c>dotnet CSharpApp.Api.dll --health-probe</c> calls this instance's liveness endpoint and
/// exits 0 (healthy) or 1, without building the host.
/// </summary>
/// <remarks>
/// ADR-0017: the <c>aspnet</c> runtime image has no curl or wget, and the chiseled one has no shell, so the app probes
/// itself. The port comes from <c>ASPNETCORE_HTTP_PORTS</c>, so the URL is built rather than written down.
/// </remarks>
public static class HealthProbe
{
    /// <summary>The command-line switch that runs the probe instead of the API.</summary>
    public const string Argument = "--health-probe";

    private const int DefaultPort = 8080;

    /// <summary>The first port in an <c>ASPNETCORE_HTTP_PORTS</c> value (<c>8080</c> or <c>8080;8081</c>); 8080 otherwise.</summary>
    public static int PortFrom(string? httpPorts)
    {
        var first = httpPorts?.Split(';')[0].Trim();
        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= IPEndPoint.MaxPort
            ? port
            : DefaultPort;
    }

    /// <summary>Calls <c>http://127.0.0.1:{port}/health/live</c>; returns 0 on a 2xx answer, 1 on anything else.</summary>
    /// <param name="port">The local port the API listens on.</param>
    /// <param name="timeout">How long to wait for the answer; the container health check has its own, longer one.</param>
    /// <param name="ct">Cancels the call.</param>
    public static async Task<int> RunAsync(int port, TimeSpan timeout, CancellationToken ct = default)
    {
        // Loopback, not localhost: the name may resolve to ::1 first. AllowedHosts must list 127.0.0.1 (compose, image).
        var uri = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), port, "health/live").Uri;
        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(uri, ct);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
