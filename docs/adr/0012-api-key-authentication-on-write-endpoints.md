# ADR-0012: API-key authentication on write endpoints only

Status: Accepted\
Date: 2026-11-23

## Context

The API calls the upstream with one service account, so without inbound authentication anyone could create upstream
data through it (the confused deputy problem). The task asks only for upstream JWT; this adds a key on writes.

## Decision

- `ApiAuth:Keys`: a non-empty list of `ApiClientKey(ClientId, Key)`, each key at least 32 characters; several keys allow
  rotation. An `IValidateOptions` check covers every item on start and names the setting, never the key.
- Scheme `ApiKey`, header `X-Api-Key`. Both sides are SHA-256 hashed and compared with `FixedTimeEquals` against every
  configured key. Success adds the claim `client_id`; no header is no result; a wrong key fails.
- Policy `WriteAccess` guards both `POST` endpoints: 401 ProblemDetails with `WWW-Authenticate: ApiKey`. GET stays
  public (ASM-07). OpenAPI declares the scheme on the POST operations.
- Development has a fixed local key in `appsettings.Development.json`; Docker maps `API_KEY` from `.env`.

## Alternatives considered

- **No inbound auth:** the confused deputy stays open. **OAuth2/JWT:** needs an identity provider.

## Consequences

- An anonymous POST gets 401 and never reaches the upstream. The key is never logged.
- `client_id` partitions the rate limiter (ADR-0016), so a client keeps one budget across IPs.

## Links

ASM-07. Tests: `CreateProduct_NoApiKey_Returns401_NoUpstreamCall`, `ValidKey_SucceedsWithClientId`,
`ApiKey_NotWrittenToLogs`, `ApiKeyClient_PartitionedByClientId_NotIp`.
