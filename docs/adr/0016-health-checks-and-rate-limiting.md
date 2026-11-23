# ADR-0016: Liveness and cached authenticated readiness; rate limiting by client

Status: Accepted\
Date: 2026-11-23

## Context

The task asks for health checks for the API itself and the upstream (ASM-12). In Docker the mock requires a token on
every resource, so an anonymous probe would always fail. Behind a proxy the client IP arrives in `X-Forwarded-For`,
which a client can forge. TLS ends at a reverse proxy (ASM-09).

## Decision

- `/health/live` covers `self`; `/health/ready` adds `upstream`; the JSON lists each check's status and duration only.
- `UpstreamHealthCheck` calls `GET categories?limit=1` through a named client (no retry, `HealthCheckTimeoutSeconds` 3)
  **with the bearer handler**: 2xx is Healthy, anything else Unhealthy, cached for `HealthCheckCacheSeconds` 10.
- Fixed-window rate limit, `RateLimiting:PermitLimit` 100 per `WindowSeconds` 60, on `/api`; 429 ProblemDetails with
  `Retry-After`; health is exempt. Partition key: remote IP (else `"anonymous"`); `client_id` once ADR-0012 is built.
- `UseForwardedHeaders` trusts only `ForwardedHeaders:KnownProxies`; with none configured it forwards nothing, because
  ASP.NET Core trusts every sender when both known lists are empty.
- Pipeline: forwarded headers, request timing, exception handler, status-code pages, security headers, HSTS, rate
  limiter, endpoints (a 429 carries the security headers and a timing line).

## Alternatives considered

- **Anonymous probe:** always Unhealthy against the mock. **Trust any forwarded header:** a client escapes its limit.

## Consequences

- Behind a proxy all clients share one IP partition until its address is listed in `KnownProxies` (the README says
  so). Readiness also proves the upstream login. The limiter state is per instance (ASM-08).

## Links

ASM-08, ASM-09, ASM-12. Tests: `HealthReady_TokenRequiringUpstream_Returns200`, `HealthReady_UpstreamDown_Returns503`,
`WithinCacheWindow_CallsUpstreamOnce`, `Api_OverLimit_Returns429`, `ForwardedFor_FromUnknownProxy_Ignored`.
