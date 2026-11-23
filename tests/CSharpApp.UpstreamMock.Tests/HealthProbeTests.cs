using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.UpstreamMock.Tests;

/// <summary>The mock's container health probe: a real socket to its liveness endpoint, exit code 0 or 1.</summary>
public sealed class HealthProbeTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly WebApplicationFactory<Program> _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task HealthProbe_ReturnsExitCode()
    {
        _factory.UseKestrel(0);
        _factory.StartServer();
        using var client = _factory.CreateClient();

        Assert.Equal(0, await HealthProbe.RunAsync(client.BaseAddress!.Port, Timeout, Ct));
        Assert.Equal(1, await HealthProbe.RunAsync(1, Timeout, Ct));
    }

    [Theory]
    [InlineData(null, 8080)]
    [InlineData("5000;6000", 5000)]
    [InlineData("abc", 8080)]
    public void PortFrom_FirstValidPortOrDefault(string? httpPorts, int expected) =>
        Assert.Equal(expected, HealthProbe.PortFrom(httpPorts));
}
