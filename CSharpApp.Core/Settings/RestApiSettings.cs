using System.ComponentModel.DataAnnotations;

namespace CSharpApp.Core.Settings;

/// <summary>Upstream API settings: base address, relative resource paths and credentials (section <c>RestApiSettings</c>).</summary>
/// <remarks>
/// Validated on start (F-11). Paths are relative to <see cref="BaseUrl"/>: a leading '/' would drop the base path (F-03).
/// Credentials are never in the committed <c>appsettings.json</c> (F-04, ASM-15): user-secrets locally, <c>.env</c> in Docker.
/// </remarks>
public sealed class RestApiSettings
{
    public const string SectionName = "RestApiSettings";

    private const string RelativePath = "^[^/].*";
    private const string CredentialsHint = "For Docker copy .env.example to .env; for local runs use user-secrets.";

    /// <summary>Absolute upstream base address ending with '/', for example <c>http://localhost:5081/api/v1/</c>.</summary>
    [Required(ErrorMessage = "RestApiSettings:BaseUrl is empty. Set the upstream base address, for example http://localhost:5081/api/v1/.")]
    [Url(ErrorMessage = "RestApiSettings:BaseUrl must be an absolute http(s) URL.")]
    [RegularExpression(".*/$", ErrorMessage = "RestApiSettings:BaseUrl must end with '/', or relative paths drop its last segment.")]
    public string BaseUrl { get; init => field = value.Trim(); } = string.Empty;

    [Required(ErrorMessage = "RestApiSettings:Products is empty.")]
    [RegularExpression(RelativePath, ErrorMessage = "RestApiSettings:Products must be relative to BaseUrl (no leading '/').")]
    public string Products { get; init => field = value.Trim(); } = string.Empty;

    [Required(ErrorMessage = "RestApiSettings:Categories is empty.")]
    [RegularExpression(RelativePath, ErrorMessage = "RestApiSettings:Categories must be relative to BaseUrl (no leading '/').")]
    public string Categories { get; init => field = value.Trim(); } = string.Empty;

    [Required(ErrorMessage = "RestApiSettings:Auth is empty.")]
    [RegularExpression(RelativePath, ErrorMessage = "RestApiSettings:Auth must be relative to BaseUrl (no leading '/').")]
    public string Auth { get; init => field = value.Trim(); } = string.Empty;

    [Required(ErrorMessage = "RestApiSettings:RefreshToken is empty.")]
    [RegularExpression(RelativePath, ErrorMessage = "RestApiSettings:RefreshToken must be relative to BaseUrl (no leading '/').")]
    public string RefreshToken { get; init => field = value.Trim(); } = string.Empty;

    [Required(ErrorMessage = "RestApiSettings:Profile is empty.")]
    [RegularExpression(RelativePath, ErrorMessage = "RestApiSettings:Profile must be relative to BaseUrl (no leading '/').")]
    public string Profile { get; init => field = value.Trim(); } = string.Empty;

    /// <summary>Upstream login e-mail (the public demo account, ASM-03).</summary>
    [Required(ErrorMessage = "RestApiSettings:Username is empty. " + CredentialsHint)]
    public string Username { get; init; } = string.Empty;

    /// <summary>Upstream login password.</summary>
    [Required(ErrorMessage = "RestApiSettings:Password is empty. " + CredentialsHint)]
    public string Password { get; init; } = string.Empty;
}
