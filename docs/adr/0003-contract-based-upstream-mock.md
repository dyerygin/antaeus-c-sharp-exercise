# ADR-0003: Own contract-based upstream mock; WireMock for faults

Status: Accepted\
Date: 2026-11-23

## Context

The upstream named in the task is offline: `api.escuelajs.co` returns Heroku's "No such app" page and the docs host no
longer resolves (checked 2026-11-20). The client agreed to a lightweight local mock of the original contract (ASM-02).
The contract was measured against the original backend built from its public source at `12bbc93` (ASM-01, 04, 05, 06,
10): unknown ids return 400 `EntityNotFoundError`, create echoes a fractional price but stores an integer.

## Decision

- `mocks/CSharpApp.UpstreamMock`: a standalone .NET 10 Minimal API for the subset under `/api/v1` (login, refresh-token,
  profile; products and categories getAll, getOne, create), with those quirks, in-memory seed data and `/health/live`.
- Settings: `MockAuth:RequireTokenOnResources` (default `false`), `MockAuth:AccessTokenMinutes` (default 28 800),
  `MockAuth:SigningKey`; HS256 JWTs without an extra package. WireMock.Net injects delays, 5xx and 401 in tests.

## Alternatives considered

- **A live provider (DummyJSON, FakeStoreAPI):** none has category ids and create plus expiring tokens.
- **WireMock mappings only:** no create-then-get state. **Original backend only:** no licence, never requires tokens.

## Consequences

- Verification is against the mock and WireMock, not the original service (the README says so).
- Drift is a risk: the contract was measured once, during analysis; the contract tests pin what was measured.

## Links

ASM-01, 02, 04, 05, 06, 10. Test: `Products_WithoutToken_Returns401_WhenRequireTokenOnResources`.
