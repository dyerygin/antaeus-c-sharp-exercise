using System.Security.Cryptography;
using System.Text;

namespace CSharpApp.Api.Security;

/// <summary>Compares a presented API key with a configured one without leaking where they differ.</summary>
public static class ApiKeyComparer
{
    /// <summary>True only when both keys are equal.</summary>
    /// <remarks>
    /// Both sides are hashed first, so the constant-time comparison always sees 32 bytes: neither the content nor the
    /// length of the configured key shows in the timing (ADR-0012).
    /// </remarks>
    public static bool Matches(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
