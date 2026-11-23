using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Api.Validation;

/// <summary>A single string must be an absolute <c>http</c> or <c>https</c> URL (ADR-0008); the list variant is <see cref="AbsoluteHttpUrlsAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class AbsoluteHttpUrlAttribute() : ValidationAttribute("{0} must be an absolute http or https URL.")
{
    public override bool IsValid(object? value) => value is null || (value is string text && AbsoluteHttpUrlsAttribute.IsAbsoluteHttpUrl(text));
}
