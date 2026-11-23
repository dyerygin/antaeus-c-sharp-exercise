# ADR-0014: Upstream token handling: cache, single-flight login, refresh then re-login, one 401 retry

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
  readable `exp`; concurrent callers share one in-flight renewal (refresh first, then login) that runs on no caller's
  token, so one caller's timeout or abort cannot cancel it for the others; each caller stops waiting on its own token,
  and a failed renewal is replaced on the next call. `Invalidate` drops only the token that was rejected.
- `BearerTokenHandler` on the products and categories clients, inside the resilience handler: it sets the Bearer header
  and, on a 401, invalidates the token and resends **once** with a new one (the body buffered before the first send).
  This is the one exception to "never retry POST" (ADR-0004): the upstream rejects a 401 before processing it. A second
  401 is returned and mapped to 502 (ADR-0005), so a revoked account cannot loop.

## Alternatives considered

- **Re-login only:** simpler, and the fallback either way. **A lock-free cache:** concurrent first calls all log in.
  **A lock held around the login:** the login then runs on the first caller's token and dies with its attempt timeout.

## Consequences

- Concurrent first requests cause one login; a hanging login ends at its own timeout; the cache is per instance.
- An expired or revoked token costs one extra upstream call; the resend counts inside the attempt's timeout.

## Links

ASM-04, ASM-06, ASM-15. Tests: `GetTokenAsync_ConcurrentCallers_LoginOnce`, `OneMinuteToken_LoginOnceThenReuse`,
`Login_Hangs_TimesOutAndReleasesLock`, `CallerCancelledDuringLogin_LoginStillCachesToken`,
`Invalidate_StaleToken_KeepsCurrent`, `Send_401Twice_RetriesOnce`,
`Send_Post401_ResendsSameBody`, `Create_Upstream401Twice_ResentOnceThenUnavailable`.
