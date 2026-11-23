namespace CSharpApp.Api.Idempotency;

/// <summary>The result of looking up a key: the outcome and, for a replay, the stored response.</summary>
public sealed record IdempotencyLookup(IdempotencyOutcome Outcome, StoredResponse? Response = null);
