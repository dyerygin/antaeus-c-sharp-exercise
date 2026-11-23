using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Core.Settings;

/// <summary>List paging defaults (section <c>Paging</c>).</summary>
/// <remarks>ADR-0010: the default page size is 20; the maximum of 100 is a contract on the request, not a setting.</remarks>
public sealed class PagingSettings
{
    public const string SectionName = "Paging";

    /// <summary>Page size when the caller sends no <c>limit</c>.</summary>
    [Range(1, 100)]
    public int DefaultLimit { get; init; } = 20;
}
