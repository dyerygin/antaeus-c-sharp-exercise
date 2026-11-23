using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CSharpApp.UpstreamMock;

/// <summary>Issues and validates HS256 JWTs, so clients see real tokens with an <c>exp</c> claim.</summary>
public sealed class MockTokens(IOptions<MockAuthSettings> options, TimeProvider time)
{
    public const string Access = "access";
    public const string Refresh = "refresh";

    // ASM-04: the original's refresh token lives 10 hours.
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromHours(10);
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}""");

    private byte[] Key => Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public MockContracts.TokenPair Issue(int userId) => new(
        Create(userId, Access, TimeSpan.FromMinutes(options.Value.AccessTokenMinutes)),
        Create(userId, Refresh, RefreshLifetime));

    /// <summary>Returns the user id of a valid, unexpired token of the expected type; otherwise null.</summary>
    public int? Validate(string token, string expectedType)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var expected = Sign($"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[2])))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            var root = payload.RootElement;
            var valid = string.Equals(root.GetProperty("typ").GetString(), expectedType, StringComparison.Ordinal)
                && root.GetProperty("exp").GetInt64() > time.GetUtcNow().ToUnixTimeSeconds();
            return valid ? root.GetProperty("sub").GetInt32() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private string Create(int userId, string type, TimeSpan lifetime)
    {
        var now = time.GetUtcNow();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = userId,
            typ = type,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(lifetime).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("N"),
        });
        var unsigned = $"{Base64Url.EncodeToString(Header)}.{Base64Url.EncodeToString(payload)}";
        return $"{unsigned}.{Sign(unsigned)}";
    }

    private string Sign(string unsigned) => Base64Url.EncodeToString(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(unsigned)));
}
