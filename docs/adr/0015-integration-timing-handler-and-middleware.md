# ADR-0015: Integration timing as a DelegatingHandler plus an ASP.NET Core middleware

Status: Accepted\
Date: 2026-11-23

## Context

The task asks for a middleware that measures and logs the performance of the integration. An ASP.NET Core middleware
sees only inbound requests; the integration is the outbound `HttpClient` pipeline, whose middleware is a
`DelegatingHandler` (ASM-13). The base code logged one line per product with interpolated messages (F-10).

## Decision

- `UpstreamTimingHandler` on every upstream client (products, categories, auth) logs
  `Upstream {Client} {Method} {Path} -> {StatusCode} in {ElapsedMs} ms` (Information; Warning at or above
  `SlowThresholdMs`, 1000 by default; Error on an exception, then rethrown) and increments the counter
  `csharpapp.upstream.requests` (tags `client`, `status`) on `Meter("CSharpApp.Upstream")`. Path only, no query, no headers.
- `RequestTimingMiddleware` logs the one inbound line `HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms`; it
  replaces Serilog's request logging, so each request has one line.
- Handler order: timing outermost, then resilience, then bearer (ADR-0014). The middleware runs before the exception
  handler, so its time and status include error mapping. `[LoggerMessage]` only.

## Alternatives considered

- **Inbound middleware only:** cannot separate upstream time. **A Mediator behaviour:** times handlers, not calls.
- **OpenTelemetry export:** needs a collector; listed under future improvements.

## Consequences

- The upstream time includes retries, the token and the 401 resend: what the caller waits for. The counter is read with
  `dotnet-counters monitor --counters CSharpApp.Upstream`; nothing exports it yet.

## Links

ASM-13, F-10. Tests: `LogsClientMethodPathStatusElapsed`, `SlowResponse_LogsWarning`, `Handler_IncrementsRequestCounter`,
`DoesNotLogQueryOrAuthorization`, `Middleware_LogsElapsed`, `UpstreamClients_HaveTimingHandlerOutermost`.
