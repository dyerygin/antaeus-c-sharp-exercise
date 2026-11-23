# ADR-0014: Upstream token handling: cache, single-flight login, refresh then re-login

Status: Accepted\
Date: 2026-11-23

## Context

The task asks for upstream JWT auth with the configured credentials (ASM-15; `Username` maps to `email`, F-19). Login
returns `access_token` and `refresh_token`; access tokens last 20 days, refresh tokens 10 hours (ASM-04). The original
guards only `auth/profile` (ASM-06), so the 401 path is exercised against our mock and WireMock.

## Decision

- `AuthClient` (no bearer, no retry, `AuthTimeoutSeconds` = 10) logs in and refreshes; `JwtExpiry.Read` reads `exp`
  without verifying the signature (we only forward the token). Tokens and credentials are never logged.
- `AccessTokenProvider` (singleton) caches until `exp` minus `min(60 s, 10 % of the lifetime)`, or 5 minutes without a
  readable `exp`; a `SemaphoreSlim` with a double check gives one login for concurrent callers and is released in
  `finally`; refresh first, then login. `Invalidate` drops only the token that was rejected.

## Alternatives considered

- **Re-login only:** simpler, and the fallback either way. **A lock-free cache:** concurrent first calls all log in.

## Consequences

- Concurrent first requests cause one login; a hanging login ends at its own timeout; the cache is per instance.

## Links

ASM-04, ASM-06, ASM-15. Tests: `GetTokenAsync_ConcurrentCallers_LoginOnce`, `OneMinuteToken_LoginOnceThenReuse`,
`Login_Hangs_TimesOutAndReleasesLock`, `Invalidate_StaleToken_KeepsCurrent`.
