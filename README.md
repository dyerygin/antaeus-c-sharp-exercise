# CSharpApp: products and categories API over the Platzi Fake Store contract

**Summary.**
An ASP.NET Core 10 Minimal API that serves products and categories from the Platzi Fake Store API: getAll, getOne
and create for both resources.
Headline decisions: a local mock of the original upstream contract, because the public API is offline;
typed `HttpClient`s with timeouts, GET-only retries and a circuit breaker; an API key on writes and idempotent creates
with an `Idempotency-Key`; .NET 10 / C# 14 in every project.
Run it in one minute with [Quick start](#quick-start-docker), then follow the 5-minute walkthrough in
[docs/DEMO.md](docs/DEMO.md).

## Overview

[![CI](https://github.com/dyerygin/antaeus-c-sharp-exercise/actions/workflows/ci.yml/badge.svg)](https://github.com/dyerygin/antaeus-c-sharp-exercise/actions/workflows/ci.yml)

The service is a thin, hardened proxy in front of the upstream store API:

- products: list (paged), get by id, create (the stored product is read back);
- categories: list (limited), get by id, create;
- upstream JWT authentication with a cached token, refresh, and one resend after a 401;
- timing logs for every upstream call and every inbound request, and a counter of upstream requests;
- liveness and readiness health checks;
- an API key on the write endpoints, idempotent creates, rate limiting and security headers;
- ProblemDetails (RFC 7807) with a `traceId` for every error.

The original task statement is kept verbatim in [docs/TASK.md](docs/TASK.md).
The assessment of the starting code, with the status of each finding, is in
[docs/CODE-ASSESSMENT.md](docs/CODE-ASSESSMENT.md).

**Final status:** everything the task asks for is implemented and tested, and so are the optional extras (API key,
idempotency, end-to-end tests against the mock, a newman smoke run against the containers). What is not done is listed
under [Unfinished work](#unfinished-work-and-known-limitations).

## Quick start (Docker)

Requirements: Docker with Compose v2. No .NET SDK is needed for this path.

bash:

```bash
cp .env.example .env
docker compose up --build -d --wait
curl -s http://localhost:8080/health/ready
```

PowerShell:

```powershell
Copy-Item .env.example .env
docker compose up --build -d --wait
curl.exe -s http://localhost:8080/health/ready
```

In PowerShell, call `curl.exe`, not `curl`: in Windows PowerShell 5.1 `curl` is an alias for `Invoke-WebRequest`.

What starts:

| Service | Host port | What it is |
| --- | --- | --- |
| `api` | `8080` (`API_PORT` in `.env`) | this API |
| `upstream-mock` | `8081` (`MOCK_PORT` in `.env`) | the contract mock of the original upstream, with tokens required on every resource |

`.env` holds the demo upstream credentials and a demo API key (`API_KEY`). They are demo values, not secrets, and
`.env` is git-ignored. Compose refuses to start without them. `/health/ready` answers
`{"status":"Healthy",…}` with a `self` and an `upstream` check. Stop with `docker compose down`.

## Run locally

Requirements: .NET SDK 10.0.100 or later (`global.json` rolls forward to the latest 10.0 feature band).

1. Start the upstream mock (terminal 1). It listens on `http://localhost:5081`.

   ```bash
   dotnet run --project mocks/CSharpApp.UpstreamMock
   ```

2. Store the demo upstream credentials in user-secrets, once. They are not in `appsettings*.json`.

   ```bash
   dotnet user-secrets set "RestApiSettings:Username" "john@mail.com" --project CSharpApp.Api
   dotnet user-secrets set "RestApiSettings:Password" "changeme" --project CSharpApp.Api
   ```

3. Start the API (terminal 2) on `http://localhost:5225`. In Development it points at the mock on `localhost:5081`
   and accepts the local dev API key from `appsettings.Development.json` (a documented demo value).

   ```bash
   dotnet run --project CSharpApp.Api
   ```

The commands are the same in bash and PowerShell.
`CSharpApp.Api/CSharpApp.Api.http` has ready-made requests for VS Code (REST Client) and Visual Studio.
If a setting is missing or invalid, start-up fails with a message that says what to set and where.

## Tests

```bash
dotnet test
```

With coverage (Microsoft.Testing.Platform + coverlet):

```bash
dotnet test --coverlet --coverlet-output-format cobertura
```

| Project | What it proves | What is stubbed |
| --- | --- | --- |
| `tests/CSharpApp.UnitTests` (152) | handlers, clients, token provider, bearer and timing handlers, health check, API-key handler, idempotency store, validation attributes, settings | `HttpMessageHandler` stubs, `FakeTimeProvider`, NSubstitute |
| `tests/CSharpApp.IntegrationTests` (125) | the whole HTTP pipeline through `WebApplicationFactory`: validation, error mapping, resilience, auth flow, API key, idempotency, health, rate limiting, hardening; 4 end-to-end tests against the in-process mock | upstream replaced by WireMock.Net on a random port, or by the mock in-process |
| `tests/CSharpApp.UpstreamMock.Tests` (24) | the mock honours the original contract | nothing |

No test calls a live upstream. Time-dependent logic uses `FakeTimeProvider`; concurrency tests hold the upstream open
with a gate, so calls truly overlap. Key tests were checked by mutation: removing the protection makes them fail (the
login gate, the retry count, auto-redirects, the bearer handler, the 401 resend, idempotent replay and 409, the cached
health check, the authentication order).

**Smoke test against the containers.** A Postman collection with assertions (`docs/postman/`, 13 requests, 56
assertions) runs with newman in the CI `docker` job. Locally, with the Docker stack up:

```bash
npx --yes newman@6.2.3 run docs/postman/CSharpApp.postman_collection.json \
  -e docs/postman/CSharpApp.local.postman_environment.json \
  --env-var "apiKey=$(grep '^API_KEY=' .env | cut -d= -f2-)"
```

The collection can also be imported into Postman (set `apiKey` in the environment).

| Measure | Value |
| --- | --- |
| Tests | 301: 152 unit, 125 integration, 24 mock contract |
| Line coverage (application projects) | 97.8 % (980 of 1002 lines), measured 2026-11-23 on `6e33f6d` |
| Line coverage (mock) | 89.7 % |

| Area | Line coverage |
| --- | --- |
| Endpoints, error mapping, middleware, rate limiting, validation, API key, health | 100 % |
| Upstream auth (token provider, bearer handler, auth client) | 98.4 % |
| Idempotency | 96.7 % |
| Upstream clients | 96.4 % |
| Composition (`Program`, DI) | 94.3 % |
| Timing handler | 93.9 % |

The original `postman.json` and `insomnia.json` target the dead upstream. They are kept unchanged as contract
references only.

## Quality gates

- **Build:** `TreatWarningsAsErrors`, .NET analyzers at `latest-recommended`, SonarAnalyzer.CSharp and
  Meziantou.Analyzer, zero warnings. Every suppression carries a comment with its reason.
- **Format:** `dotnet format --verify-no-changes`.
- **CI** (`.github/workflows/ci.yml`): locked restore, build, format, tests with a Cobertura coverage artifact, the
  vulnerable-package check (`dotnet list package --vulnerable --include-transitive`, parsed as JSON because the command
  exits 0 on findings); lint with hadolint, actionlint, markdownlint and gitleaks (history and working tree) from
  pinned images; a `docker` job that builds both images, starts the stack and runs newman; Conventional Commits on
  pull requests.
- **Dependabot** for NuGet, Docker base images and GitHub Actions.
- **Independent reviews:** three interim reviews and a final review of the whole diff, each by a fresh reviewer
  without the implementer's reasoning; the final review added a second model and a security review. Each finding was
  fixed with a test, rejected with evidence, or deferred here.

| Review | Findings | Fixed | Rejected with evidence | Deferred |
| --- | --- | --- | --- | --- |
| Foundations (layers, clients, resilience, errors) | 1 important, 4 minor | 4 | 1 | 0 |
| Features (endpoints, validation, tokens, concurrency) | 2 important, 7 minor | 2 | 0 | 7 |
| Operations (timing, health, rate limiting, Docker, CI) | 1 important, 5 minor | 2 | 0 | 4 |
| Final, whole diff | 2 important, 4 minor | 3 | 0 | 3 |
| Second model (final) | 1 | 0 | 1 | 0 |
| Security review (final) | 0 | — | — | — |

The newman run against the containers found one more defect (validation errors named `Title` instead of `title`),
fixed with exact-name tests. SonarQube and Stryker.NET were not run (time box); see Future improvements.

## API

Base path: `/api/v1`. Errors are always `application/problem+json` with a `traceId`.

| Method and route | Success | Errors |
| --- | --- | --- |
| `GET /api/v1/products?offset=0&limit=20` | 200, array | 400 invalid `offset`/`limit`, 429, 502, 504 |
| `GET /api/v1/products/{id}` | 200 | 400 invalid id, 404, 429, 502, 504 |
| `POST /api/v1/products` | 201 with `Location` | 400, 401, 409, 413, 415, 422, 429, 502, 504 |
| `GET /api/v1/categories?limit=20` | 200, array | 400, 429, 502, 504 |
| `GET /api/v1/categories/{id}` | 200 | 400, 404, 429, 502, 504 |
| `POST /api/v1/categories` | 201 with `Location` | 400, 401, 409, 413, 415, 422, 429, 502, 504 |
| `GET /health/live` | 200 | — |
| `GET /health/ready` | 200 | 503 when the upstream is unhealthy |
| `GET /openapi/v1.json`, `/openapi/v1.yaml` | 200 when `OpenApi:Enabled` is true | 404 otherwise |

Paging: `limit` defaults to 20 and must be 1–100; `offset` defaults to 0. Validation errors name the fields as they
appear in JSON (`errors.title`, `errors.limit`).

**API key.** The two `POST` endpoints require the `X-Api-Key` header. A missing or wrong key returns 401 with
`WWW-Authenticate: ApiKey`, and the request never reaches the upstream. GET endpoints are public. Several keys can be
configured (`ApiAuth:Keys:N:ClientId` and `…:Key`, at least 32 characters), so a key can be rotated without downtime.
Docker takes the key from `API_KEY` in `.env`.

**Idempotency.** The create endpoints accept an optional `Idempotency-Key` header (1–100 visible ASCII characters,
no spaces):

| Situation | Result |
| --- | --- |
| no header | normal create |
| new key | create; a 2xx response is stored for 24 hours |
| same key, same body | the stored response is replayed with `Idempotent-Replayed: true`; the upstream is not called |
| same key, different body | 422 |
| same key while the first request is still running | 409 |
| first request failed | the key is released, so the client can retry |
| client gave up while the create was running | the create still finishes and is stored; the retry replays it |

```bash
curl -i -X POST http://localhost:8080/api/v1/categories \
  -H "X-Api-Key: $API_KEY" -H "Idempotency-Key: demo-001" -H "Content-Type: application/json" \
  -d '{"name":"Demo","image":"https://example.com/demo.png"}'
```

OpenAPI 3.1 is served at `/openapi/v1.json` and `/openapi/v1.yaml` when `OpenApi:Enabled` is true (Development and
Docker). It describes every status above, the `ApiKey` security scheme on the POST operations, and the
`Idempotency-Key` header (maximum length 100).

## Upstream and mock

### The public API named in the task is unreachable

Re-checked on 2026-11-23:

- `http://api.escuelajs.co/api/v1/` returns Heroku's "No such app" page (`404`, `Server: heroku-router`);
  over HTTPS the TLS handshake fails with `unrecognized name`. No application is attached to the domain any more.
- The documentation site `fakeapi.platzi.com` has no address record.
- Public web archives show the API last answering in spring 2026; the backend repository
  `PlatziLabs/fake-api-backend` has had no commits since March 2025.
- Live alternatives (DummyJSON, EverREST, FakeStoreAPI) do not match the contract (categories without ids or without
  create, no refresh tokens, a different data shape); details in [docs/CODE-ASSESSMENT.md](docs/CODE-ASSESSMENT.md).
- The exercise owners were informed and agreed, on 2026-11-20, to a lightweight local mock of the original contract.

**All verification was done against the mock and WireMock.** The contract itself was measured during analysis against
the original backend, built locally from its public source (ASM-01, ASM-04, ASM-05, ASM-06, ASM-10). Nothing was
verified against the public service.

### Why both the original backend and our own mock

| Piece | Role |
| --- | --- |
| Original backend, built from its public source during analysis | how the contract was measured: status codes, error bodies, token lifetimes, price storage |
| Own mock (`mocks/CSharpApp.UpstreamMock`) | reproduces that contract in .NET; runs in the tests and in the default compose; can require tokens on every resource, so the refresh path is exercised |
| WireMock.Net (tests only) | produces the failures neither can produce on demand: timeouts, 5xx, delays, 302, malformed bodies |

The original backend cannot be the default: it has no licence (so it is not shipped) and it does not guard resources
with a token. The mock is small, licence-clean and deterministic.

### Contract sources

- the task README and the supplied Postman/Insomnia collections;
- the original backend source at commit `12bbc93`;
- behaviour measured by running that backend locally.

What the mock covers, its settings and its limits:
[mocks/CSharpApp.UpstreamMock/README.md](mocks/CSharpApp.UpstreamMock/README.md).
`MockAuth:RequireTokenOnResources` (true in Docker) makes products and categories require a bearer token, which the
original only does for `auth/profile`.

### Pointing the app at a real service

Set `RestApiSettings__BaseUrl` to the service's `/api/v1/` URL (with the trailing slash), and the credentials through
`RestApiSettings__Username` and `RestApiSettings__Password` (environment variables, user-secrets or `.env` with
compose). Paths in `RestApiSettings` are relative, without a leading slash.

## Architecture and conventions

The four layers of the starting code are kept:

| Layer | Holds | Depends on |
| --- | --- | --- |
| `CSharpApp.Core` | DTOs, client interfaces, settings, upstream exceptions | nothing |
| `CSharpApp.Application` | CQRS queries, commands and handlers (Mediator) | Core |
| `CSharpApp.Infrastructure` | typed `HttpClient`s, resilience, token provider, delegating handlers, upstream health check | Core |
| `CSharpApp.Api` | endpoints, request records and validation, exception handler, middleware, API-key auth, idempotency filter, rate limiting, health endpoints | all |

`mocks/CSharpApp.UpstreamMock` is standalone: it references none of the app projects.

| Kind | Form |
| --- | --- |
| DTOs, request bodies, queries, commands, results | `sealed record` |
| Handlers, clients, HTTP handlers, middleware, filters, health checks | `sealed class` with a primary constructor |
| Settings | `sealed class` with `{ get; init; }` and data annotations, validated on start |
| Exceptions | `sealed` subclasses of one `abstract UpstreamException` |
| Async | `CancellationToken` last, `Async` suffix |
| Logging | source-generated `[LoggerMessage]`; never tokens, passwords, API keys or query strings |
| Packages | Central Package Management, NuGet lock files, `--locked-mode` in CI and Docker |
| Commits | Conventional Commits; the body says why and links the ADR or assumption id |

Every architectural or behavioural decision has an ADR in [docs/adr/](docs/adr/). The contract surface (endpoint
records, options, Core interfaces) has XML docs that say why, with the ADR or assumption id.

## Assumptions

The full register, with evidence, impact if wrong and where each one is relied on:
[docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md).

| ID | Assumption | Status |
| --- | --- | --- |
| ASM-01 | The upstream contract is the one implemented by the original backend at `12bbc93` | verified against the original backend (2026-11-20) |
| ASM-02 | All verification is against our mock and WireMock, because the public service is offline | unverified |
| ASM-03 | The credentials in the task are a public demo account, not a secret | verified against the original backend (2026-11-20) |
| ASM-04 | Access tokens last 20 days, refresh tokens 10 hours | verified against the original backend (2026-11-20) |
| ASM-05 | Create echoes a fractional price but stores an integer (`10.5` → `10`) | verified against the original backend (2026-11-20) and the mock |
| ASM-06 | Only `auth/profile` requires a token upstream | verified against the original backend (2026-11-20) |
| ASM-07 | GET endpoints may stay public; only writes need an API key | unverified |
| ASM-08 | One instance: the token cache and the idempotency store are in memory | unverified |
| ASM-09 | TLS is terminated by a reverse proxy in production | unverified |
| ASM-10 | Upstream 400 with `name = EntityNotFoundError` means "not found" | verified against the original backend (2026-11-20) and the mock |
| ASM-11 | Category create does not normalise fields, so no read-after-write is needed | unverified |
| ASM-12 | "Health check for the api and self" means upstream readiness plus our own liveness | unverified |
| ASM-13 | Feature #4 "middleware" covers outbound timing; an inbound middleware is added as well | unverified |
| ASM-14 | Update and delete are out of scope | unverified |
| ASM-15 | "Use the credentials provided to appsettings.json" means configuration, not the committed file | unverified |

ASM-15 deviates from the task wording on purpose: the credentials moved to user-secrets and `.env`, so no credential
sits in a committed file.

## Design decisions and trade-offs

The starting code and its defects are described in [docs/CODE-ASSESSMENT.md](docs/CODE-ASSESSMENT.md).

- **Typed clients and resilience (ADR-0004).** One typed `HttpClient` per upstream area through `IHttpClientFactory`,
  on a `SocketsHttpHandler` with pooled connections and a 2-minute connection lifetime (DNS changes are picked up).
  The standard resilience pipeline adds total (10 s) and per-attempt (3 s) timeouts, retries and a circuit breaker.
  Retries are disabled for unsafe methods: a POST is never retried by the pipeline. Auto-redirects are off.
- **Mediator for CQRS (ADR-0007).** `Mediator` (source-generated, MIT) instead of MediatR (commercial licence from
  version 13) or a hand-written dispatcher. In a thin proxy CQRS mostly buys a uniform shape and testable handlers;
  there is no separate read model.
- **Built-in validation (ADR-0008).** .NET 10 Minimal API validation with DataAnnotations on request records instead
  of FluentValidation; invalid input returns 400 before any handler runs. Request bodies are read strictly: duplicate
  or unknown members and `null` in non-nullable members are rejected (ADR-0006).
- **Upstream token provider (ADR-0014).** The token is cached until min(60 s, 10 % of its lifetime) before its `exp`.
  Concurrent callers share one renewal, which runs on no caller's cancellation token. Refresh is tried first, login is
  the fallback. On an upstream 401 the token is invalidated and the request is resent once; a second 401 is not
  retried. This is the one case where a POST is resent: the upstream rejected it before processing.
- **Timing (ADR-0015).** A `DelegatingHandler`, outermost on every upstream client, logs and counts each call; an
  ASP.NET Core middleware, first in the pipeline, logs each inbound request. The task asks for a "middleware": the
  delegating handler is the middleware of the outbound pipeline, and the only place that sees each upstream call.
- **Error mapping (ADR-0005).**

  | Condition | Response |
  | --- | --- |
  | invalid request | 400 ValidationProblem with field `errors` |
  | malformed JSON, duplicate or unknown JSON members | 400 |
  | wrong `Content-Type` | 415 |
  | body larger than 64 KB | 413 |
  | upstream 404, or upstream 400 with `EntityNotFoundError` | 404 |
  | unknown `categoryId` on product create | 400 on `categoryId` |
  | other upstream 400 | 400 |
  | upstream 401/403, 3xx, 5xx, unreachable, unreadable body, open circuit | 502 |
  | upstream timeout | 504 |
  | client aborted the request | 499, no body |
  | anything else | 500 with a generic title, no exception message |

- **Shared DTOs (ADR-0009).** The upstream DTOs are also the API response models. It saves time and mapping code; the
  cost is that an upstream shape change reaches our clients.
- **API key on writes only (ADR-0012).** Without it, anyone could create upstream data through our service account
  (a confused deputy). Reads stay public, as the upstream's are.
- **Idempotency (ADR-0013).** Clients retry creates after timeouts; without a key that creates duplicates. The scope is
  client id + method + route + key; the hash covers the bound request re-serialized, so a reformatted retry is still a
  replay. A started create runs to its end even if the client goes away, so the retry replays it. The store is in
  memory behind `IIdempotencyStore`; Redis is the production choice.
- **Read-after-write on product create (ADR-0011).** The upstream echoes a fractional price but stores an integer. The
  handler reads the product back on its own timeout and returns what was stored; if the read-back fails, it returns
  the echo and logs a warning, because the create already happened. Categories need no read-back.
- **Default page size (ADR-0010).** 20 by default, at most 100, so a list call never pulls the whole catalogue.
- **Health and rate limiting (ADR-0016).** Liveness checks the process only; readiness also probes the upstream with
  an authenticated call (3 s timeout), cached for 10 s. Rate limiting (100 requests per 60 s) partitions by API client,
  else by remote IP; forwarded headers are trusted only from configured proxies.
- **Health probe without curl (ADR-0017).** The runtime image has no curl or wget. Both apps accept `--health-probe`
  and exit 0 or 1, so the Docker health check needs no extra tools and would work on a chiseled image too.
- **Contract mock (ADR-0003)** and **layers and conventions (ADR-0001, ADR-0002)**: see above.

## .NET 10 / C# 14 features used

| Feature | Where |
| --- | --- |
| `net10.0` and C# 14 in every project; one `Directory.Build.props`; `global.json` | `Directory.Build.props`, `global.json` |
| `.slnx` solution format | `CSharpApp.slnx` |
| Built-in Minimal API validation (`AddValidation()`) | `CSharpApp.Api/DependencyInjection.cs` |
| OpenAPI 3.1 with XML doc comments, response descriptions and a YAML endpoint | `CSharpApp.Api/Program.cs`, `Endpoints/EndpointResponses.cs` |
| Strict request JSON (`AllowDuplicateProperties`, `UnmappedMemberHandling`, `RespectNullableAnnotations`) | `CSharpApp.Api/DependencyInjection.cs` |
| C# 14 extension members (`extension(IServiceCollection services)`) | every `Add…`/`Map…` registration, e.g. `CSharpApp.Infrastructure/DependencyInjection.cs` |
| C# 14 `field` keyword | `CSharpApp.Core/Settings/RestApiSettings.cs` |
| `WebApplicationFactory.UseKestrel()` for real-socket tests | `tests/CSharpApp.IntegrationTests/RequestBodyTests.cs`, `HealthProbeTests.cs` |
| `dotnet test` on Microsoft.Testing.Platform, `coverlet.MTP` | `global.json`, test projects |
| Built-in authentication and authorization meters | `dotnet-counters monitor --counters Microsoft.AspNetCore.Authentication,Microsoft.AspNetCore.Authorization` |

Available since .NET 8/9 and used as well: `TimeProvider`, `IExceptionHandler`, `TypedResults`,
`Microsoft.Extensions.Http.Resilience`, the rate limiter, `[LoggerMessage]`, `System.Threading.Lock`, primary
constructors and collection expressions.

Tools are run with `dnx` (.NET 10), for example `dnx dotnet-counters monitor --counters CSharpApp.Upstream`;
the classic `dotnet tool install --global dotnet-counters` works as well.

## Security concerns and solutions

| Risk | Done | Remaining |
| --- | --- | --- |
| Credentials in source control | moved to user-secrets and `.env`; settings validated on start | a secret store such as Key Vault |
| Anyone can create upstream data through our account (confused deputy) | API key on write endpoints, hashed constant-time comparison, several keys for rotation, keys validated on start | OAuth2/JWT for clients |
| Upstream JWT `exp` read without verifying the signature | used only to schedule renewal; the upstream still validates the token | — |
| Tokens, keys or credentials in logs | never logged; request logs hold method, path, status and time only (tested) | an audit log |
| Duplicate creates | no POST retries in the pipeline; `Idempotency-Key` with replay, 409 and 422 | a shared store for several instances |
| Error responses leak internals | ProblemDetails with a generic title; upstream bodies are never echoed | — |
| Malformed or oversized input | 64 KB body limit, 415 for a wrong content type, strict JSON, validation | — |
| Browser-side attacks | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store` on errors, no `Server` header, HSTS outside Development | a CORS policy if a browser client appears |
| Host header abuse | `AllowedHosts` is `localhost` by default, a fixed list in Docker, `*` only in Development | list the real host name in production |
| Abuse and IP spoofing | fixed-window rate limiting; forwarded headers trusted only from known proxies | CIDR ranges for proxies |
| Container compromise | non-root user, read-only root filesystem, all capabilities dropped, `no-new-privileges`, memory and CPU limits | chiseled, digest-pinned images |
| Redirect to an unexpected host | auto-redirects off on upstream clients | — |
| Vulnerable dependencies | vulnerable-package check in CI, Dependabot, lock files | — |

Demo values, on purpose: the mock's signing key (`appsettings.json` of the mock), the local dev API key
(`appsettings.Development.json`, loaded only in Development) and the demo credentials and key in `.env.example`.
Compose publishes the mock on port 8081 for the demo; a real deployment would not expose it. The supplied
`postman.json`/`insomnia.json` contain expired sample tokens of the old public service (F-20); they are baselined in
`.gitleaksignore`.

## Performance concerns and solutions

- **Connection pooling and DNS refresh.** `IHttpClientFactory` with a pooled `SocketsHttpHandler` replaces a shared,
  mutated `HttpClient`; connections are recycled every 2 minutes.
- **Streamed JSON.** Responses are deserialized from the stream, not read into a string first.
- **Timeouts and circuit breaker.** A hung upstream returns 504 within the total timeout, also when the body stalls; a
  failing one opens the circuit and fails fast with 502.
- **Bounded lists.** The default page size of 20 (maximum 100) stops a list call from pulling the whole catalogue.
- **Cached readiness.** The upstream health result is cached for 10 s, so frequent probes do not load the upstream.
- **Visibility.** Every upstream call logs `Upstream {Client} {Method} {Path} -> {StatusCode} in {ElapsedMs} ms`
  (time to response headers, a warning above 1 s) and increments the counter `csharpapp.upstream.requests` (meter
  `CSharpApp.Upstream`); every inbound request logs its total time.
- **Next step:** caching GET responses (the data changes rarely).

No load test was run, and no performance figure is claimed here. The one measured figure is the container health
probe: about 0.4 s per run, hence the 30 s interval.

## Upstream quirks and how they are handled

- **Fractional price.** Create echoes `10.5` but the upstream stores `10`. Product create reads the product back and
  returns the stored value; `Price` is `decimal` (the starting code's `int?` failed on a fraction).
- **400 instead of 404.** An unknown id returns 400 with `"name": "EntityNotFoundError"`. It is mapped to 404; an
  unknown `categoryId` on create becomes a 400 on that field.
- **Only `auth/profile` is guarded.** The original accepts product and category calls without a token, so the
  401 → refresh path can only be shown against our mock with `RequireTokenOnResources=true`, and in WireMock tests.

## Unfinished work and known limitations

- The token cache and the idempotency store are in memory: one instance only, lost on restart.
- An upstream timeout (504) on a create releases the idempotency key, although the upstream may have created the item.
- Update and delete are not implemented (the task names getAll, getOne and create).
- Upstream DTOs double as API response models.
- No architecture tests; no Mediator pipeline behaviours.
- The request counter is recorded but not exported (no OpenTelemetry exporter).
- Only API version 1 exists.
- The supplied Postman/Insomnia collections target the dead upstream.
- Deferred review items (test hardening): the 504 test does not assert the elapsed bound; one concurrency unit test
  waits with a short delay instead of a signal; the readiness cache does not merge concurrent probes after it expires;
  a caller's cancellation is logged at Error by the timing handler; `KnownProxies` takes single addresses, no CIDR
  ranges; CI's `--wait` gates on liveness, not readiness.

## AI usage

The implementation was AI-assisted under the author's direction and review.

- **Tool:** Claude Code (Claude Opus) as the coding assistant; fresh Claude Opus sessions as independent reviewers;
  OpenAI Codex as a second-model review at the end.
- **Areas:** the analysis of the starting code and of the offline upstream, the plan, code and tests in every layer,
  the mock, Docker and CI, and the documentation drafts.
- **Why:** to fit the full scope, with tests and documentation, into the time box.
- **How it was checked:** a written plan fixed the scope and the decisions before any code; every behaviour change
  started from a failing test or a recorded mutation; every commit passed the build with warnings as errors, the format
  check and the full test suite; reviewers without the implementer's reasoning checked the diff three times during the work
  and at the end, and every finding was fixed, rejected with evidence or listed here; the README claims were checked
  against the code and a fresh-clone run of [docs/DEMO.md](docs/DEMO.md). The decisions and trade-offs are the
  author's.
- No AI attribution is added to commits.

## Future improvements

- A distributed token cache and idempotency store (Redis) behind the existing interfaces.
- OAuth2/JWT for clients instead of API keys; a CORS policy; secrets in a key vault; an audit log.
- A load test (for example k6) to put measured numbers behind the performance notes.
- Property-based API tests (for example Schemathesis) against the OpenAPI document.
- An automated parity check of the mock against the original backend, to detect contract drift.
- A local SonarQube run and mutation testing (Stryker.NET) to report a quality gate and a mutation score.
- GET response caching; separate response models, decoupled from upstream DTOs.
- OpenTelemetry export of traces and metrics.
- Update and delete endpoints if the business needs them; architecture tests for the layer rules.
- The chiseled, digest-pinned runtime image in production; no published mock port outside the demo.
- The deferred review items listed under Unfinished work.
- Replace the original Postman/Insomnia collections with this API's collection.
