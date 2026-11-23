# ADR-0006: Request hardening: body limit, strict inbound JSON, security headers

Status: Accepted\
Date: 2026-11-23

## Context

The API takes JSON from callers and forwards creates under our service account. The base commit has no security
headers, no HSTS and `AllowedHosts: *` (F-14). With duplicate JSON keys, which value wins is a parser detail, so a
validator and a later reader could see different values. TLS ends at a reverse proxy (ASM-09).

## Decision

- Inbound JSON uses the `JsonSerializerOptions.Strict` settings: duplicate or unknown members and `null` in non-nullable
  members are rejected; numbers are never read from strings. Responses are camelCase without `null` members. The
  upstream clients keep lenient options, because the upstream adds fields of its own.
- Kestrel `MaxRequestBodySize` from `ApiLimits:MaxRequestBodyBytes` = 65 536 (413); a non-JSON body gets 415. The
  statuses are the same in Development and Production.
- `SecurityHeadersMiddleware` sets `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and
  `Cache-Control: no-store` on errors; Kestrel's `Server` header is off; HSTS outside Development.
- `AllowedHosts`: `localhost` by default, `*` only in Development (else 400).

## Alternatives considered

- **Headers only at the proxy:** the demo runs direct. **Duplicate-key check alone:** misses unknown members.

## Consequences

- Oversized, malformed and ambiguous bodies are rejected before validation or any upstream call; a client sending
  extra fields gets 400. TestServer does not enforce the body limit, so the 413 test runs on Kestrel.

## Links

Finding F-14; ASM-09. Tests: `AnyResponse_HasSecurityHeaders`, `ErrorResponse_IsNotCached`, `Response_HasNoServerHeader`,
`Response_UsesCamelCase_OmitsNulls`, `Request_HostHeader_FilteredOutsideDevelopment`, `RequestBodyTests`.
