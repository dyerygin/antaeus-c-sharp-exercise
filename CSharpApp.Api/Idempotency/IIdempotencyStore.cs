namespace CSharpApp.Api.Idempotency;

/// <summary>Remembers idempotency keys and the responses they produced (ADR-0013).</summary>
/// <remarks>The in-memory implementation serves one instance (ASM-08); a shared store such as Redis fits behind this interface.</remarks>
public interface IIdempotencyStore
{
    /// <summary>Claims <paramref name="scopeKey"/> for a request whose body hashes to <paramref name="requestHash"/>, or reports why not.</summary>
    ValueTask<IdempotencyLookup> TryBeginAsync(string scopeKey, string requestHash, CancellationToken ct);

    /// <summary>Stores the response of a started key, so later requests replay it.</summary>
    ValueTask CompleteAsync(string scopeKey, StoredResponse response, CancellationToken ct);

    /// <summary>Releases a started key without a response, so the client can retry.</summary>
    ValueTask AbandonAsync(string scopeKey, CancellationToken ct);
}
