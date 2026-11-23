using CSharpApp.Api.Idempotency;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSharpApp.UnitTests.Idempotency;

public sealed class InMemoryIdempotencyStoreTests
{
    private static readonly StoredResponse Created = new(201, "http://localhost/api/v1/products/42", "{\"id\":42}"u8.ToArray(), "application/json");

    private readonly FakeTimeProvider _time = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private InMemoryIdempotencyStore Store(int maxEntries = 10_000) =>
        new(Options.Create(new IdempotencySettings { TtlHours = 24, MaxEntries = maxEntries }), _time);

    [Fact]
    public async Task TryBegin_New_Started()
    {
        var lookup = await Store().TryBeginAsync("scope", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Started, lookup.Outcome);
    }

    [Fact]
    public async Task TryBegin_Completed_Replay()
    {
        var store = Store();
        await store.TryBeginAsync("scope", "hash", Ct);
        await store.CompleteAsync("scope", Created, Ct);

        var lookup = await store.TryBeginAsync("scope", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Replay, lookup.Outcome);
        Assert.Same(Created, lookup.Response);
    }

    [Fact]
    public async Task TryBegin_SameKeyDifferentHash_HashMismatch()
    {
        var store = Store();
        await store.TryBeginAsync("scope", "hash", Ct);
        await store.CompleteAsync("scope", Created, Ct);

        var lookup = await store.TryBeginAsync("scope", "other-hash", Ct);

        Assert.Equal(IdempotencyOutcome.HashMismatch, lookup.Outcome);
    }

    [Fact]
    public async Task TryBegin_InFlight_InProgress()
    {
        var store = Store();
        await store.TryBeginAsync("scope", "hash", Ct);

        var lookup = await store.TryBeginAsync("scope", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.InProgress, lookup.Outcome);
    }

    [Fact]
    public async Task Expired_StartsAgain()
    {
        var store = Store();
        await store.TryBeginAsync("scope", "hash", Ct);
        await store.CompleteAsync("scope", Created, Ct);

        _time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var lookup = await store.TryBeginAsync("scope", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Started, lookup.Outcome);
    }

    [Fact]
    public async Task Abandon_AllowsRetry()
    {
        var store = Store();
        await store.TryBeginAsync("scope", "hash", Ct);
        await store.AbandonAsync("scope", Ct);

        var lookup = await store.TryBeginAsync("scope", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Started, lookup.Outcome);
    }

    [Fact]
    public async Task Full_EvictsOldestCompleted_NeverInFlight()
    {
        var store = Store(maxEntries: 2);
        await store.TryBeginAsync("in-flight", "hash", Ct);
        await store.TryBeginAsync("done", "hash", Ct);
        await store.CompleteAsync("done", Created, Ct);

        var third = await store.TryBeginAsync("third", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Started, third.Outcome);
        Assert.Equal(IdempotencyOutcome.InProgress, (await store.TryBeginAsync("in-flight", "hash", Ct)).Outcome);
        Assert.Equal(IdempotencyOutcome.Started, (await store.TryBeginAsync("done", "hash", Ct)).Outcome);
    }

    [Fact]
    public async Task Full_AllInFlight_StillClaimsTheKey()
    {
        // Nothing can be evicted, yet the new key is claimed: a concurrent retry must see InProgress, not start again.
        var store = Store(maxEntries: 1);
        await store.TryBeginAsync("first", "hash", Ct);

        var started = await store.TryBeginAsync("second", "hash", Ct);
        var retry = await store.TryBeginAsync("second", "hash", Ct);

        Assert.Equal(IdempotencyOutcome.Started, started.Outcome);
        Assert.Equal(IdempotencyOutcome.InProgress, retry.Outcome);
    }
}
