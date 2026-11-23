# ADR-0017: Health probe without curl

Status: Accepted\
Date: 2026-11-23

## Context

Compose starts the API only when the mock is healthy (`depends_on: service_healthy`). The runtime image `aspnet:10.0`
has neither `curl` nor `wget`, and the chiseled variant has no shell. A literal probe URL breaks when the port changes.

## Decision

- The API and the mock accept `--health-probe`: before the host is built, `HealthProbe` calls
  `http://127.0.0.1:<port>/health/live` with a 2 s timeout and exits 0 (2xx) or 1. The port is the first value of
  `ASPNETCORE_HTTP_PORTS` (default 8080).
- `HEALTHCHECK` and the compose `healthcheck` run `dotnet <app>.dll --health-probe`: `interval 30s`, `timeout 5s`,
  `retries 3`, `start_period 20s`, `start_interval 2s`. The image allows the host `127.0.0.1`; compose sets the full list.
- Images: multi-stage, locked restore in its own layer, `aspnet:10.0` runtime as user 1654 (`app`); compose adds a
  read-only root with a `/tmp` tmpfs, `cap_drop: ALL`, `no-new-privileges`, 512 MB and one CPU per service.

## Alternatives considered

- **Install curl:** a larger attack surface. **Shell-based probe:** impossible in the chiseled image.
- **No container health check:** compose could not wait for a healthy upstream and API.

## Consequences

- One probe starts a `dotnet` process: 0.4 s wall-clock measured, so the 30 s interval keeps the cost low.
- Docker 29.9 honours `start_interval`: `docker compose up --wait` reports both services healthy in about 6 s.
- The chiseled image (124 MB instead of 246 MB, no shell) is a one-line change of the runtime `FROM`.

## Links

ADR-0016. Tests: `HealthProbe_ReturnsExitCode` (API and mock, against Kestrel), `HealthProbe_Unhealthy503_Returns1`.
