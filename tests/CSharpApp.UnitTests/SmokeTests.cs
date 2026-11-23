namespace CSharpApp.UnitTests;

public sealed class SmokeTests
{
    [Fact]
    public void TestHost_Runs_ExecutesTests() => Assert.NotNull(typeof(SmokeTests).Assembly.EntryPoint);
}
