namespace CSharpApp.Api.Idempotency;

/// <summary>What <see cref="IIdempotencyStore.TryBeginAsync"/> found for a key.</summary>
public enum IdempotencyOutcome
{
    /// <summary>A new key: the caller runs the request and then completes or abandons it.</summary>
    Started,

    /// <summary>The same key and body completed before: replay the stored response.</summary>
    Replay,

    /// <summary>The same key is still running: 409.</summary>
    InProgress,

    /// <summary>The key was used with a different body: 422.</summary>
    HashMismatch,
}
