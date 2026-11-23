namespace CSharpApp.Api.Idempotency;

/// <summary>Header names and the key format of idempotent creates (ADR-0013).</summary>
public static class IdempotencyHeaders
{
    /// <summary>The request header that carries the client's key.</summary>
    public const string Key = "Idempotency-Key";

    /// <summary>The response header that marks a replayed response.</summary>
    public const string Replayed = "Idempotent-Replayed";

    /// <summary>The longest accepted key.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>1 to 100 visible ASCII characters, no spaces (a UUID fits).</summary>
    public static bool IsValidKey(string key) =>
        key.Length is > 0 and <= MaxKeyLength && key.All(c => c is > ' ' and <= '~');
}
