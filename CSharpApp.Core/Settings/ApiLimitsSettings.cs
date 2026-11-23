using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Core.Settings;

/// <summary>Inbound request limits (section <c>ApiLimits</c>), validated on start.</summary>
/// <remarks>ADR-0006: a product create is well under 64 KB; a larger body gets 413 before it is read into memory.</remarks>
public sealed class ApiLimitsSettings
{
    public const string SectionName = "ApiLimits";

    /// <summary>Kestrel's <c>MaxRequestBodySize</c>.</summary>
    [Range(1_024, 10_485_760)]
    public long MaxRequestBodyBytes { get; init; } = 65_536;
}
