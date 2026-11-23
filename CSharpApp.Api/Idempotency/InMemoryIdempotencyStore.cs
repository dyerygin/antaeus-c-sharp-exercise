using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Idempotency;

/// <summary>Idempotency keys in process memory with a TTL and an entry cap (ADR-0013, ASM-08).</summary>
/// <remarks>One lock keeps check-and-claim atomic, so two concurrent requests with one key cannot both start.</remarks>
public sealed class InMemoryIdempotencyStore(IOptions<IdempotencySettings> settings, TimeProvider time) : IIdempotencyStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ValueTask<IdempotencyLookup> TryBeginAsync(string scopeKey, string requestHash, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_entries.TryGetValue(scopeKey, out var entry) && entry.ExpiresAt > now)
            {
                var lookup = entry switch
                {
                    _ when !string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal) => new IdempotencyLookup(IdempotencyOutcome.HashMismatch),
                    { Response: null } => new IdempotencyLookup(IdempotencyOutcome.InProgress),
                    _ => new IdempotencyLookup(IdempotencyOutcome.Replay, entry.Response),
                };
                return ValueTask.FromResult(lookup);
            }

            MakeRoom(now);
            _entries[scopeKey] = new Entry(requestHash, null, now.AddHours(settings.Value.TtlHours));
            return ValueTask.FromResult(new IdempotencyLookup(IdempotencyOutcome.Started));
        }
    }

    public ValueTask CompleteAsync(string scopeKey, StoredResponse response, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(scopeKey, out var entry))
            {
                _entries[scopeKey] = entry with { Response = response };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AbandonAsync(string scopeKey, CancellationToken ct)
    {
        lock (_gate)
        {
            _entries.Remove(scopeKey);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Drops expired entries, then the oldest completed one while full. In-flight entries are never dropped (that would
    /// let a duplicate start); they are bounded by the number of concurrent requests.
    /// </summary>
    private void MakeRoom(DateTimeOffset now)
    {
        if (_entries.Count < settings.Value.MaxEntries)
        {
            return;
        }

        foreach (var expired in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToList())
        {
            _entries.Remove(expired);
        }

        while (_entries.Count >= settings.Value.MaxEntries)
        {
            var completed = _entries.Where(pair => pair.Value.Response is not null).ToList();
            if (completed.Count == 0)
            {
                return;
            }

            _entries.Remove(completed.MinBy(pair => pair.Value.ExpiresAt).Key);
        }
    }

    private sealed record Entry(string RequestHash, StoredResponse? Response, DateTimeOffset ExpiresAt);
}
