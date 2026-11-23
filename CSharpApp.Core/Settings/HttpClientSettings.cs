using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Core.Settings;

/// <summary>Outbound HTTP settings: timeouts, retries and health-check timing (section <c>HttpClientSettings</c>).</summary>
/// <remarks>Feed the standard resilience pipeline (ADR-0004); validated on start.</remarks>
public sealed class HttpClientSettings : IValidatableObject
{
    public const string SectionName = "HttpClientSettings";

    /// <summary>Upper bound for one upstream call including retries.</summary>
    [Range(1, 120)]
    public int TotalTimeoutSeconds { get; init; } = 10;

    /// <summary>Timeout of a single attempt.</summary>
    [Range(1, 60)]
    public int AttemptTimeoutSeconds { get; init; } = 3;

    /// <summary>Retries for idempotent requests only (ADR-0004).</summary>
    [Range(0, 5)]
    public int RetryCount { get; init; } = 2;

    /// <summary>Base delay of the exponential back-off between retries.</summary>
    [Range(1, 10_000)]
    public int RetryBaseDelayMs { get; init; } = 100;

    /// <summary>Timeout of one upstream login or refresh call (ADR-0014), so a hanging login cannot hold the token lock.</summary>
    [Range(1, 60)]
    public int AuthTimeoutSeconds { get; init; } = 10;

    /// <summary>Timeout of the upstream readiness probe.</summary>
    [Range(1, 30)]
    public int HealthCheckTimeoutSeconds { get; init; } = 3;

    /// <summary>How long a readiness result is reused.</summary>
    [Range(0, 300)]
    public int HealthCheckCacheSeconds { get; init; } = 10;

    /// <summary>Upstream calls slower than this are logged as warnings.</summary>
    [Range(1, 60_000)]
    public int SlowThresholdMs { get; init; } = 1000;

    /// <summary>The resilience pipeline rejects an attempt timeout above the total timeout, so it is checked on start.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AttemptTimeoutSeconds > TotalTimeoutSeconds)
        {
            yield return new ValidationResult(
                "HttpClientSettings:AttemptTimeoutSeconds must not exceed TotalTimeoutSeconds.",
                [nameof(AttemptTimeoutSeconds), nameof(TotalTimeoutSeconds)]);
        }
    }
}
