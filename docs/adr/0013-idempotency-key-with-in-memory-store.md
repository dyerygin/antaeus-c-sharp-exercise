# ADR-0013: `Idempotency-Key` with an in-memory store behind an interface

Status: Accepted\
Date: 2026-11-23

## Context

We never retry a POST (ADR-0004), but clients retry after timeouts, and each retry would create a duplicate upstream.
Creates are therefore idempotent on request. The service runs as one instance (ASM-08).

## Decision

- `Idempotency-Key` is optional; when present it is 1–100 visible ASCII characters without spaces (OpenAPI
  `maxLength` 100), else 400. Scope: `client_id` (ADR-0012) + method + route + key.
- `IdempotencyFilter<TRequest>` on both POSTs hashes the **bound request re-serialized** (SHA-256), so whitespace and
  member order do not make a different body; it needs no request buffering.
- Only 2xx is stored, exactly as sent; an error abandons the key. Same key and body → the stored response with
  `Idempotent-Replayed: true`, without running the endpoint; another body → 422; still in flight → 409. The create
  and the stored response run on no caller's token, so a client that gives up during the upstream create (the reason
  clients retry) or during the read-back still finds the result on retry.
- `InMemoryIdempotencyStore`: one lock, `TimeProvider` expiry, `TtlHours` 24, `MaxEntries` 10 000 (when full, expired
  entries, then the oldest completed one, are dropped; in-flight keys never are).

## Alternatives considered

- **Hash the raw bytes:** a reformatted retry would get 422. **Redis now:** infrastructure the exercise lacks.

## Consequences

- The store is lost on restart and not shared between instances; Redis fits behind `IIdempotencyStore`.
- An upstream timeout (504) abandons the key although the upstream may have created the item; a retry may then
  create it again. Only the upstream itself could close that gap.

## Links

ADR-0004. Tests: `Create_SameKeyTwice_OneUpstreamCreate`, `Create_ConcurrentSameKey_OneSucceedsOneReturns409`,
`Create_ClientAbortsAfterUpstreamSuccess_RetryReplays`, `Create_ClientAbortsDuringUpstreamCreate_RetryReplays`,
`Full_EvictsOldestCompleted_NeverInFlight`.
