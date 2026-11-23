using System.Text;
using System.Text.Json;

namespace CSharpApp.TestData.Builders;

/// <summary>Unsigned test JWTs with a chosen <c>exp</c>; the API never verifies upstream signatures (ADR-0014).</summary>
public static class TestJwt
{
    /// <summary>A token that expires at <paramref name="expires"/>; <paramref name="id"/> makes tokens distinct.</summary>
    public static string ExpiringAt(DateTimeOffset expires, string id = "a") =>
        Create(JsonSerializer.Serialize(new { sub = 1, jti = id, exp = expires.ToUnixTimeSeconds() }));

    /// <summary>A token whose payload is the given JSON.</summary>
    public static string Create(string payloadJson) =>
        $"{Encode("""{"alg":"HS256","typ":"JWT"}""")}.{Encode(payloadJson)}.c2lnbmF0dXJl";

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
