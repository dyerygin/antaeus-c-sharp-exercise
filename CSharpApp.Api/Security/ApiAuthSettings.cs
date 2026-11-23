namespace CSharpApp.Api.Security;

/// <summary>Inbound API keys for the write endpoints (section <c>ApiAuth</c>).</summary>
/// <remarks>
/// ADR-0012: several keys may be active at once, so a key can be rotated without downtime. Validated on start by
/// <see cref="ApiAuthSettingsValidator"/>, because DataAnnotations do not look inside list items.
/// </remarks>
public sealed class ApiAuthSettings
{
    public const string SectionName = "ApiAuth";

    /// <summary>The accepted keys, one per client; at least one.</summary>
    public IReadOnlyList<ApiClientKey> Keys { get; init; } = [];
}
