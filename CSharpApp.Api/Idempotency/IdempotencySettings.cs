using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Api.Idempotency;

/// <summary>Limits of the idempotency store (section <c>Idempotency</c>, ADR-0013).</summary>
public sealed class IdempotencySettings
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a key and its stored response are kept, in hours; default 24.</summary>
    [Range(1, 168)]
    public int TtlHours { get; init; } = 24;

    /// <summary>Most keys kept at once; when full, the oldest completed entry is dropped first; default 10 000.</summary>
    [Range(1, 1_000_000)]
    public int MaxEntries { get; init; } = 10_000;
}
