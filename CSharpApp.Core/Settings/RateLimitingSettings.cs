using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Core.Settings;

/// <summary>Fixed-window rate limit of the <c>/api</c> endpoints (section <c>RateLimiting</c>), validated on start.</summary>
/// <remarks>ADR-0016: per remote IP, so one client cannot exhaust the upstream for the others; health is exempt.</remarks>
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests allowed per partition and window.</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 100;

    /// <summary>Window length.</summary>
    [Range(1, 3_600)]
    public int WindowSeconds { get; init; } = 60;
}
