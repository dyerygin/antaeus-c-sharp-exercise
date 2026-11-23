using CSharpApp.Infrastructure.Auth;
using CSharpApp.TestData.Builders;

namespace CSharpApp.UnitTests.Auth;

public sealed class JwtExpiryTests
{
    [Fact]
    public void JwtExpiry_ReadsExp()
    {
        var expires = new DateTimeOffset(2026, 12, 13, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(expires, JwtExpiry.Read(TestJwt.ExpiringAt(expires)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.!!!.c")]
    public void JwtExpiry_Garbage_ReturnsNull(string token) => Assert.Null(JwtExpiry.Read(token));

    [Theory]
    [InlineData("""{"sub":1}""")]
    [InlineData("""{"exp":"tomorrow"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void JwtExpiry_NoNumericExp_ReturnsNull(string payload) => Assert.Null(JwtExpiry.Read(TestJwt.Create(payload)));
}
