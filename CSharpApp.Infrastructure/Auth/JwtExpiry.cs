using System.Buffers.Text;
using System.Text.Json;

namespace CSharpApp.Infrastructure.Auth;

/// <summary>Reads the <c>exp</c> claim of a JWT.</summary>
/// <remarks>
/// The signature is not verified: the API only forwards the token to the service that issued it, and uses <c>exp</c>
/// solely to renew in time (ADR-0014). A token without a readable <c>exp</c> is still usable; the cache then picks a
/// short lifetime.
/// </remarks>
public static class JwtExpiry
{
    /// <summary>The expiry, or <c>null</c> when the token is not a readable JWT with a numeric <c>exp</c>.</summary>
    public static DateTimeOffset? Read(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("exp", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
