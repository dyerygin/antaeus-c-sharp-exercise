# ADR-0004: Typed HttpClients with the standard resilience pipeline; idempotent retries only; no redirects

Status: Accepted\
Date: 2026-11-23

## Context

The base service creates its own `HttpClient` and sets `BaseAddress` per call, so every request after the first returns
500 (F-01). There is no timeout, retry or circuit breaker (F-05): a dead upstream held a request about 8 s, a slow one
100 s. Bodies are buffered as strings without a `CancellationToken` (F-09). A retried POST could create a duplicate.

## Decision

- Typed clients (`AddHttpClient<IProductsClient, ProductsClient>`, same for categories); `BaseAddress` set once; primary
  handler `SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = 2 min }`.
- `AddStandardResilienceHandler` from `HttpClientSettings` (`TotalTimeoutSeconds` 10, `AttemptTimeoutSeconds` 3 and not
  above the total, `RetryCount` 2, `RetryBaseDelayMs` 100, validated on start) and `Retry.DisableForUnsafeHttpMethods()`.
- `GetAsync` → status check → streamed `ReadFromJsonAsync`; `ct` passed through; a `null` body is a contract error.

## Alternatives considered

- **Refit:** an extra dependency for six calls. **`Microsoft.Extensions.Http.Polly`:** the legacy Polly v7.

## Consequences

The pipeline never retries POST; GETs survive one transient 5xx. Timeouts, open circuits and upstream errors still
surface as exceptions until the error mapping (ADR-0005) turns them into ProblemDetails.

## Links

Findings F-01, F-05, F-09. Tests: `Create_Upstream500_NotRetried`, `GetAll_Upstream500ThenOk_RetriedAndSucceeds`,
`PrimaryHandler_IsSocketsHttpHandler_WithoutAutoRedirect`, `AttemptTimeoutAboveTotal_FailsValidation`.
