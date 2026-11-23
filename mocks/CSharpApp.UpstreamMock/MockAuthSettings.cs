using System.ComponentModel.DataAnnotations;

namespace CSharpApp.UpstreamMock;

/// <summary>Authentication behaviour of the mock (section <c>MockAuth</c>).</summary>
/// <remarks>The defaults reproduce the original backend (ASM-04, ASM-06); Docker tightens them (ADR-0003).</remarks>
public sealed class MockAuthSettings
{
    public const string SectionName = "MockAuth";

    /// <summary>Require a bearer token on products and categories. The original never does (ASM-06), so the default is false.</summary>
    public bool RequireTokenOnResources { get; init; }

    /// <summary>Access token lifetime in minutes; 28 800 = 20 days, as the original (ASM-04).</summary>
    [Range(1, 525_600)]
    public int AccessTokenMinutes { get; init; } = 28_800;

    /// <summary>HMAC-SHA256 key for the mock's JWTs: a demo value, not a secret.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;
}
