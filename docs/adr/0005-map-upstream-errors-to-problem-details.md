# ADR-0005: Map upstream errors to ProblemDetails, including 400 EntityNotFoundError → 404

Status: Accepted\
Date: 2026-11-23

## Context

In the base commit every upstream failure becomes HTTP 500 with an empty body (F-07) and a null or malformed body throws
(F-08). The upstream reports an unknown id as 400 with `"name":"EntityNotFoundError"` (ASM-10), so mapping by status
alone turns every missing product into 400. Errors must never echo upstream bodies, tokens, keys or credentials.

## Decision

- `EnsureUpstreamSuccessAsync`: 404, or 400 with `EntityNotFoundError` → `UpstreamNotFoundException`; other 400/422 →
  `UpstreamBadRequestException` (upstream `message` kept for logs only); 401/403, 3xx, 5xx → `UpstreamUnavailableException`.
  Transport errors, including a reset mid-body → unavailable; a 2xx with an empty, `null` or malformed body →
  `UpstreamContractException`. Headers and the streamed body share one `TotalTimeoutSeconds` deadline →
  `UpstreamTimeoutException`; the resilience pipeline's own timeout and open circuit are translated to the same Core
  types, so Application never sees Polly.
- `ApiExceptionHandler` writes ProblemDetails with `traceId`: `RequestValidationException` → 400 ValidationProblem;
  `BadHttpRequestException` → its status; not found → 404; upstream bad request → 400; unavailable, contract error or
  broken circuit → 502; either timeout → 504; client abort → 499 without a body; anything else → 500 with a generic title.

## Alternatives considered

- **Pass upstream status and body through**, or **map by status only:** leaks details; unknown ids become 400.

## Consequences

- An upstream 401/403 is our credential problem, so the caller sees 502. If ASM-10 is wrong, an unknown id returns 400.
- Development throws `BadHttpRequestException` for unreadable bodies; the mapping gives the same status as Production.

## Links

Findings F-07, F-08; ASM-10. Tests: `UpstreamResponseExtensionsTests`, `ApiExceptionHandlerTests`,
`GetProducts_UpstreamFailure_ReturnsProblemWithTraceId`, `GetProducts_UpstreamDelayBeyondTimeout_Returns504`,
`GetById_BodyStallsBeyondTimeout_ThrowsUpstreamTimeout`.
