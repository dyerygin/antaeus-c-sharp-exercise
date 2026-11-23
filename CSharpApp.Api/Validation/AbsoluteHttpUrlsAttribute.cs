using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Api.Validation;

/// <summary>Every item of a string list must be an absolute <c>http</c> or <c>https</c> URL (ADR-0008).</summary>
/// <remarks>A <c>null</c> list is valid here, so <c>[Required]</c> decides; other schemes (<c>javascript:</c>, <c>ftp:</c>) are rejected.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AbsoluteHttpUrlsAttribute() : ValidationAttribute("Every item of {0} must be an absolute http or https URL.")
{
    public override bool IsValid(object? value) =>
        value is null || (value is IEnumerable<string?> items && items.All(IsAbsoluteHttpUrl));

    /// <summary>True for an absolute URL with the http or https scheme.</summary>
    internal static bool IsAbsoluteHttpUrl(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
