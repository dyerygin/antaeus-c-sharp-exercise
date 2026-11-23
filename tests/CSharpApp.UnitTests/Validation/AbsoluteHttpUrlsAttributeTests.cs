using CSharpApp.Api.Validation;

namespace CSharpApp.UnitTests.Validation;

public sealed class AbsoluteHttpUrlsAttributeTests
{
    private static readonly AbsoluteHttpUrlsAttribute Attribute = new();

    [Fact]
    public void AbsoluteHttpUrls_HttpAndHttps_AreValid() =>
        Assert.True(Attribute.IsValid(new List<string> { "https://images.test/a.png", "http://images.test/b.png" }));

    [Fact]
    public void AbsoluteHttpUrls_Null_IsLeftToRequired() => Assert.True(Attribute.IsValid(null));

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path.png")]
    [InlineData("ftp://images.test/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void AbsoluteHttpUrls_InvalidItem_IsInvalid(string item) =>
        Assert.False(Attribute.IsValid(new[] { "https://images.test/a.png", item }));

    [Fact]
    public void AbsoluteHttpUrls_NullItem_IsInvalid() =>
        Assert.False(Attribute.IsValid(new string?[] { null }));

    [Fact]
    public void AbsoluteHttpUrls_NotAStringList_IsInvalid() => Assert.False(Attribute.IsValid(42));
}
