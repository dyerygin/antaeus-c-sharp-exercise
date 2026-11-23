using CSharpApp.Api.Health;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpApp.IntegrationTests;

/// <summary>The container health probe (ADR-0017): a real socket to the liveness endpoint, exit code 0 or 1.</summary>
public sealed class HealthProbeTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task HealthProbe_ReturnsExitCode()
    {
        // The probe opens a socket, so the API runs on Kestrel, not TestServer (R9, R47).
        _factory.UseKestrel(0);
        _factory.StartServer();
        using var client = _factory.CreateClient();

        var exitCode = await HealthProbe.RunAsync(client.BaseAddress!.Port, Timeout, Ct);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task HealthProbe_NothingListening_Returns1()
    {
        // Port 1 is closed on CI and developer machines: connection refused at once (L85).
        var exitCode = await HealthProbe.RunAsync(1, Timeout, Ct);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task HealthProbe_Unhealthy503_Returns1()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/health/live").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));

        var exitCode = await HealthProbe.RunAsync(server.Port, Timeout, Ct);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("8080", 8080)]
    [InlineData("5000;6000", 5000)]
    [InlineData("abc", 8080)]
    [InlineData("0", 8080)]
    [InlineData("70000", 8080)]
    public void PortFrom_FirstValidPortOrDefault(string? httpPorts, int expected) =>
        Assert.Equal(expected, HealthProbe.PortFrom(httpPorts));
}
