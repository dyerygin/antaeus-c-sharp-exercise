using CSharpApp.Api.Validation;

namespace CSharpApp.UnitTests.Validation;

public sealed class AbsoluteHttpUrlAttributeTests
{
    private static readonly AbsoluteHttpUrlAttribute Attribute = new();

    [Theory]
    [InlineData("https://images.test/a.png")]
    [InlineData("http://images.test/a.png")]
    public void AbsoluteHttpUrl_HttpOrHttps_IsValid(string value) => Assert.True(Attribute.IsValid(value));

    [Fact]
    public void AbsoluteHttpUrl_Null_IsLeftToRequired() => Assert.True(Attribute.IsValid(null));

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative.png")]
    [InlineData("ftp://images.test/a.png")]
    [InlineData("javascript:alert(1)")]
    public void AbsoluteHttpUrl_Invalid_IsInvalid(string value) => Assert.False(Attribute.IsValid(value));
}
