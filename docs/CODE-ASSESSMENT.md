# Code assessment of the supplied baseline

This is the review of the code as it was supplied, before any change: commit `cf97a27` ("Initial Commit") on `main`,
reviewed on 2026-11-20. It records what was wrong, how each problem was found, and where the solution addresses it.
The `Status` column in section 5.1 records how each finding was resolved on the branch `feature/solution`.

## 1. Summary

The baseline is a .NET 9 Web API that proxies one operation (`GET products`) of the public Platzi Fake Store API.
The review found **20 findings: 1 critical, 6 high, 9 medium, 4 low**.

The most important results:

1. **The only endpoint stops working after its first request** (F-01). A singleton service mutates
   `HttpClient.BaseAddress` on every call; the second and every later request return HTTP 500 until the process
   restarts. Reproduced.
2. **Creating a product with a fractional price returns HTTP 500 although the product is created** (F-02). `Price` is
   declared as `int?`. The upstream accepts `10.5`, echoes it in the create response and stores `10`, so the response
   fails deserialization. Verified against the original backend run locally.
3. **Two of three configured upstream paths resolve to wrong URLs** (F-03), which blocks the categories and JWT
   features before any code is written.
4. **The HTTP integration has no timeouts, retries or circuit breaker** although settings for them exist (F-05), and
   **upstream failures surface as empty HTTP 500 responses** (F-07).
5. **Upstream credentials are committed in plain text** (F-04).

In addition, **the third-party service is offline**: its domain no longer has a running application behind it
(section 8). The solution therefore needs a local upstream substitute for manual runs and the Docker demo.

None of the findings needs a redesign of the four-layer architecture.

## 2. Scope

**In scope.**

- All source, project and configuration files at commit `cf97a27`: `CSharpApp.Api`, `CSharpApp.Application`,
  `CSharpApp.Core`, `CSharpApp.Infrastructure`, `appsettings*.json`, `launchSettings.json`, `CSharpApp.Api.http`,
  `.gitignore`, and the supplied `postman.json` and `insomnia.json`.
- Behaviour against the upstream contract as documented by the supplied Postman collection.
- Gaps between the code and the task statement supplied with the exercise ([TASK.md](TASK.md)), section 6.

**Out of scope.**

- The upstream service itself (`api.escuelajs.co`), its availability and data quality.
- The GraphQL part of the Postman collection.
- Load and penetration testing.

## 3. Method

The review followed the structure of a technical review (IEEE 1028); categories follow ISO/IEC 25010.

| Activity | Tooling | Result |
|---|---|---|
| Manual code inspection | Line-by-line reading of every in-scope file | Findings with file and line |
| Independent second review | Separate reviewer without access to the first reviewer's conclusions | Corroborated all high findings; added F-13, F-14, F-17 details |
| Static analysis | `dotnet build` with `AnalysisLevel=latest-recommended` | 7 warnings (section 4.1) |
| Dependency audit | `dotnet list package --vulnerable --include-transitive`, `--outdated`, `--deprecated`; NuGet licence metadata | Section 4.2 |
| Secret scan | gitleaks v8.30.1 | F-20 |
| Runtime reproduction | Running the API (`ASPNETCORE_ENVIRONMENT=Production`) and calling it | F-01, F-05, F-07 reproduced |
| Serialization check | Isolated C# program with the original `Product` DTO and representative JSON | F-02, F-06, F-08 reproduced |
| Contract check | The original upstream backend, built from its public source at commit `12bbc93` and run locally | F-02, F-07 measured |

**Test environment.** macOS, .NET SDK 11.0.100, ASP.NET Core runtime 9.0.9, target framework `net9.0`.

**Evidence levels.**

- **Reproduced**: observed at runtime.
- **Confirmed**: traced in code or by a specification (for example RFC 3986, framework documentation); not executed.
- **Plausible**: likely, depends on external data or conditions not verified.

**Severity.**

| Severity | Definition |
|---|---|
| Critical | Core function unavailable or data corrupted in normal use; no workaround. |
| High | Feature blocked, security exposure, or failure under predictable conditions (upstream slow or failing). |
| Medium | Incorrect behaviour in edge cases, degraded performance or operability, maintainability risk with real cost. |
| Low | Convention, clarity or hygiene issue without functional effect. |

**Priority.**

| Priority | Meaning |
|---|---|
| Must | Must be fixed before the solution is submitted. |
| Should | Should be fixed within the exercise time budget. |
| Could | Fix if time allows; otherwise documented as a future improvement. |

## 4. Tool results

### 4.1 Static analysis

| Rule | Location | Related finding |
|---|---|---|
| NU1504 duplicate `PackageReference` `Microsoft.AspNetCore.OpenApi` | `CSharpApp.Api.csproj:12,17` | F-17 |
| CS8602 possible null dereference | `ProductsService.cs:24` | F-08 |
| CA1001 type owns disposable field but is not disposable | `ProductsService.cs:3` | F-01 |
| CA1848, CA1873, CA2254 logging performance and template | `ProductsService.cs:26` | F-10 |

ASP0000 (`BuildServiceProvider` during registration, F-11) is not reported because the call is in a class library,
where the ASP.NET Core analyzer does not run.

### 4.2 Dependencies

- **Known vulnerabilities:** none (NuGet advisory database, 2026-11-20).
- **Unused references:** `MediatR`, `MediatR.Extensions.Autofac.DependencyInjection` (adds Autofac), `FluentValidation`,
  `FluentValidation.DependencyInjectionExtensions`, `Microsoft.Extensions.Http.Polly`, `Microsoft.Extensions.Hosting`,
  `Microsoft.Extensions.Configuration`.
- **Licensing:** MediatR 12.x is Apache-2.0, and its last release, 12.5.0, came out on 2025-04-01. Versions 13.0.0 and
  later (from 2025-07-02) use a commercial licence. FluentValidation remains Apache-2.0.
- **Legacy:** `Microsoft.Extensions.Http.Polly` (Polly v7) is superseded by `Microsoft.Extensions.Http.Resilience`.

## 5. Findings

### 5.1 Register and status

`Fixed in` names the commits that fix the finding, earliest first; `Accepted` gives the reason in the column itself.
`Addressed in` names the area of the solution and the decision record that covers it.

| ID | Title | Severity | Priority | Evidence | Addressed in | Status |
|---|---|---|---|---|---|---|
| F-01 | Shared `HttpClient` mutated per call; endpoint fails after first request | Critical | Must | Reproduced | Typed clients ([ADR-0004](adr/0004-typed-http-clients-standard-resilience.md)) | Fixed in edbbf6f |
| F-02 | Fractional price in a response fails deserialization (create returns 500) | Medium | Must | Reproduced (original backend) | `decimal` price; read-after-write ([ADR-0011](adr/0011-read-after-write-for-product-create.md)) | Fixed in edbbf6f, c92d735 |
| F-03 | Leading slash in configured paths resolves to wrong upstream URLs | High | Must | Confirmed | Settings validated on start | Fixed in a7c9209 |
| F-04 | Upstream credentials committed in plain text | High | Must | Confirmed | User-secrets locally, `.env` in Docker | Fixed in a7c9209, 67e3389 |
| F-05 | No timeout, retry or circuit breaker; resilience settings unused | High | Must | Reproduced | Standard resilience pipeline ([ADR-0004](adr/0004-typed-http-clients-standard-resilience.md)) | Fixed in edbbf6f, 67345bf |
| F-06 | Product images are always empty | High | Must | Reproduced | Settable `Images` | Fixed in edbbf6f |
| F-07 | Upstream failures surface as HTTP 500 with an empty body | High | Must | Reproduced | ProblemDetails mapping ([ADR-0005](adr/0005-map-upstream-errors-to-problem-details.md)) | Fixed in 38f6238 |
| F-08 | Null or malformed upstream body causes unhandled exceptions | High | Must | Reproduced | Unusable body → 502 ([ADR-0005](adr/0005-map-upstream-errors-to-problem-details.md)) | Fixed in edbbf6f, 38f6238 |
| F-09 | No cancellation support; response body buffered as a string | Medium | Should | Confirmed | `CancellationToken` end to end; streamed JSON | Fixed in edbbf6f |
| F-10 | One log entry per product; interpolated log messages | Medium | Should | Confirmed | Source-generated `[LoggerMessage]` | Fixed in edbbf6f |
| F-11 | Second DI container built during service registration | Medium | Should | Confirmed | Options bound with `ValidateOnStart` | Fixed in a7c9209 |
| F-12 | Product list fetched without pagination | Medium | Should | Confirmed | `offset`/`limit`, default 20, max 100 ([ADR-0010](adr/0010-page-size-default-20-max-100.md)) | Fixed in edbbf6f, deb2897 |
| F-13 | Logger not flushed; startup failures unlogged; Development overrides ignored | Medium | Should | Confirmed | Serilog bootstrap logger; `Serilog` section per environment; request timing middleware | Fixed in a7c9209, 5002b15 |
| F-14 | Transport hardening missing (HTTPS redirection, HSTS, allowed hosts) | Medium | Should | Confirmed | HSTS outside Development; TLS at the proxy (ASM-09); `AllowedHosts` per environment, an unlisted `Host` gets 400 | Fixed in a7c9209, 5c2f42c |
| F-15 | Integration code placed in the Application layer | Medium | Should | Confirmed | Clients in Infrastructure behind Core interfaces ([ADR-0001](adr/0001-keep-four-layers.md), [ADR-0009](adr/0009-upstream-dtos-as-api-responses.md)) | Fixed in edbbf6f, deb2897 |
| F-16 | Target framework `net9.0` instead of the required .NET 10 | Medium | Must | Confirmed | `net10.0` in every project; `global.json` | Fixed in ee2988e |
| F-17 | Package hygiene: duplicate, unused and mismatched references | Low | Should | Confirmed | Central Package Management ([ADR-0002](adr/0002-code-conventions-cpm-analyzers.md)) | Fixed in ee2988e |
| F-18 | RPC-style route and no declared API contract | Low | Should | Confirmed | Resource routes; OpenAPI | Fixed in deb2897 |
| F-19 | Ambiguous setting names and units | Low | Could | Confirmed | Settings named with units and validated | Fixed in a7c9209, b7c7cae |
| F-20 | Sample JWTs committed in the supplied API collections | Low | Should | Confirmed (gitleaks) | `.gitleaksignore` baseline; supplied files unchanged | Accepted: the supplied files and their history stay unchanged; the fingerprints are baselined with a comment in `.gitleaksignore` (e8fb07e) |

### 5.2 Details

#### F-01 Shared `HttpClient` mutated per call; endpoint fails after first request

- **Severity / priority:** Critical / Must · **Category:** Reliability · **Evidence:** Reproduced
- **Location:** `CSharpApp.Application/Products/ProductsService.cs:12,19`;
  `CSharpApp.Infrastructure/Configuration/DefaultConfiguration.cs:13`
- **Description:** `ProductsService` creates its own `HttpClient` in the constructor and is registered as a singleton.
  `GetProducts` assigns `BaseAddress` on every call. `HttpClient` forbids changing properties after its first request.
- **Steps to reproduce:** start the API; call `GET /api/v1/getproducts` twice.
- **Expected:** both calls return the product list.
- **Actual:** the second call, and every call after it, returns HTTP 500 in about 5 ms with
  `InvalidOperationException: This instance has already started one or more requests. Properties can only be modified
  before sending the first request.`
- **Impact:** the only feature is unavailable after first use until restart. A self-created `HttpClient` also bypasses
  `IHttpClientFactory` handler management (DNS changes not picked up, no delegating handlers), cannot be replaced in
  tests, and two concurrent first requests race on the setter.
- **Recommendation:** register a typed client with `IHttpClientFactory`, set `BaseAddress` once at registration, and
  inject it.
- **Verification:** typed-client unit tests; integration test `RepeatedCalls_Succeed`.

#### F-02 Fractional price in a response fails deserialization (create returns 500)

- **Severity / priority:** Medium / Must · **Category:** Functional suitability · **Evidence:** Reproduced against the
  original upstream backend run locally (commit `12bbc93`)
- **Severity rationale:** the upstream validation (`IsPositive`) accepts a fractional price. `POST products` with
  `"price": 10.5` returns `201` with `"price": 10.5` in the body; the stored value, as later returned by
  `GET products`, is `10`. So lists contain integers only, and the defect hits the create response. Rating it Critical
  would assume fractional values persist; the measurement shows they do not.
- **Location:** `CSharpApp.Core/Dtos/ProductDto.cs:12`
- **Description:** `Price` is `int?`. System.Text.Json does not convert `19.99` to an integer and throws.
- **Steps to reproduce:** deserialize `[{"id":1,"title":"Shirt","price":19.99}]` into `List<Product>`.
- **Expected:** price `19.99`.
- **Actual:** `JsonException: The JSON value could not be converted to System.Nullable<System.Int32>`. The exception
  aborts the whole list.
- **Impact:** a create endpoint would return HTTP 500 for a valid request even though the upstream created the product,
  which invites duplicate retries by clients. Modelling money as an integer also breaks as soon as the upstream schema
  keeps decimals.
- **Recommendation:** use `decimal` for money and cover it with a deserialization test.
- **Verification:** `GetAll_DeserializesProducts_IncludingImagesAndDecimalPrice`;
  `CreateProduct_FractionalPrice_ReturnsStoredPrice`.

#### F-03 Leading slash in configured paths resolves to wrong upstream URLs

- **Severity / priority:** High / Must · **Category:** Functional suitability · **Evidence:** Confirmed (RFC 3986,
  section 5.2)
- **Location:** `CSharpApp.Api/appsettings.json:23-24`
- **Description:** `BaseUrl` is `https://api.escuelajs.co/api/v1/`. `Categories` is `/categories` and `Auth` is
  `/auth/login`. A relative reference that starts with `/` replaces the whole base path.
- **Expected:** `https://api.escuelajs.co/api/v1/categories`, `https://api.escuelajs.co/api/v1/auth/login`.
- **Actual:** `https://api.escuelajs.co/categories`, `https://api.escuelajs.co/auth/login`. These URLs do not exist.
  `Products` resolves correctly only because it has no leading slash.
- **Impact:** blocks required features #2 (categories) and #3 (JWT).
- **Recommendation:** use relative paths without a leading slash and validate settings at startup.
- **Verification:** `LeadingSlashPath_FailsValidation`.

#### F-04 Upstream credentials committed in plain text

- **Severity / priority:** High / Must · **Category:** Security · **Evidence:** Confirmed
- **Location:** `CSharpApp.Api/appsettings.json:25-26`
- **Description:** the username and password for the upstream account are stored in the base configuration file under
  source control.
- **Impact:** secrets leak through the repository, its forks and container images. They cannot be rotated per
  environment without a code change. The values are a public demo account here, but the pattern carries into real
  deployments.
- **Recommendation:** keep empty placeholders in `appsettings.json`. Supply values through user-secrets in development
  and environment variables or a secret store elsewhere. Validate them at startup. Never log them.
- **Note:** the task asks to use the credentials "from appsettings". The solution reads them from configuration, not
  from the committed file (ASM-15).

#### F-05 No timeout, retry or circuit breaker; resilience settings unused

- **Severity / priority:** High / Must · **Category:** Reliability · **Evidence:** Reproduced (timeout); Confirmed (rest)
- **Location:** `CSharpApp.Infrastructure/Configuration/HttpConfiguration.cs:5-8`;
  `CSharpApp.Core/Settings/HttpClientSettings.cs`; `appsettings.json` section `HttpClientSettings`
- **Description:** `AddHttpConfiguration` is empty. `HttpClientSettings` (`LifeTime`, `RetryCount`, `SleepDuration`) is
  bound but never read.
- **Steps to reproduce:** point the API at an unreachable upstream and call `GET /api/v1/getproducts`.
- **Expected:** a fast, bounded failure.
- **Actual:** the request hung for about 8 s until the operating system's connect timeout. With a reachable but slow
  upstream, `HttpClient` waits for its default of 100 s.
- **Impact:** request threads and connections pile up when the upstream degrades. Transient errors are not retried. A
  failing upstream is not isolated.
- **Recommendation:** use `Microsoft.Extensions.Http.Resilience` with total and per-attempt timeouts, retries for
  idempotent methods only, and a circuit breaker, configured from validated settings.
- **Verification:** `Create_Upstream500_NotRetried`, `GetAll_Upstream500ThenOk_RetriedAndSucceeds`,
  `GetProducts_UpstreamDelayBeyondTimeout_Returns504`.

#### F-06 Product images are always empty

- **Severity / priority:** High / Must · **Category:** Functional suitability · **Evidence:** Reproduced
- **Location:** `CSharpApp.Core/Dtos/ProductDto.cs:18`
- **Description:** `Images` is a getter-only list. By default System.Text.Json does not populate getter-only collection
  properties.
- **Steps to reproduce:** deserialize `[{"id":2,"price":5,"images":["b.jpg"]}]`.
- **Expected:** `Images.Count == 1`.
- **Actual:** `Images.Count == 0`.
- **Impact:** silent data loss in every product response.
- **Recommendation:** make the property settable, or use `JsonObjectCreationHandling.Populate`, and add a test.
- **Verification:** `GetAll_DeserializesProducts_IncludingImagesAndDecimalPrice`.

#### F-07 Upstream failures surface as HTTP 500 with an empty body

- **Severity / priority:** High / Must · **Category:** Reliability · **Evidence:** Reproduced
- **Location:** `CSharpApp.Api/Program.cs:11`; `ProductsService.cs:21`
- **Description:** `AddProblemDetails()` is registered, but there is no `UseExceptionHandler()` or
  `UseStatusCodePages()`. `EnsureSuccessStatusCode()` converts every upstream status, 404 included, into an exception.
- **Steps to reproduce:** run in Production with a failing upstream and call the endpoint.
- **Note:** the upstream reports an unknown id as HTTP 400 with `"name":"EntityNotFoundError"` (measured against the
  original backend), so a status-only mapping would turn every missing product into a 400 (ASM-10).
- **Expected:** a ProblemDetails response with a meaningful status: 404 for a missing resource, 400 for rejected input,
  502 or 504 for upstream failure or timeout.
- **Actual:** HTTP 500 with an empty body. In Development the developer exception page exposes stack traces.
- **Impact:** clients cannot tell their own errors from upstream outages. A getOne endpoint would return 500 for an
  unknown id. Operators get no correlation id.
- **Recommendation:** map typed upstream exceptions to ProblemDetails in an `IExceptionHandler`, include the trace id,
  and never echo upstream bodies.
- **Verification:** exception-handler unit tests; integration tests for 400, 404, 502 and 504.

#### F-08 Null or malformed upstream body causes unhandled exceptions

- **Severity / priority:** High / Must · **Category:** Reliability · **Evidence:** Reproduced
- **Location:** `ProductsService.cs:23-24`
- **Description:** the result of `JsonSerializer.Deserialize` is used without a null check.
- **Steps to reproduce:** an upstream body of `null`.
- **Actual:** `Deserialize` returns null and `res.Count` throws `NullReferenceException`. Any shape mismatch throws
  `JsonException`. Both end as HTTP 500 (see F-07).
- **Recommendation:** validate the response, treat an unusable upstream payload as an upstream failure (HTTP 502), and
  test it.
- **Verification:** `NullBody_MapsToContractError` (a contract error is answered with 502); `Upstream_MalformedJson_Returns502`.

#### F-09 No cancellation support; response body buffered as a string

- **Severity / priority:** Medium / Should · **Category:** Performance efficiency · **Evidence:** Confirmed
- **Location:** `ProductsService.cs:17-23`; `Program.cs:26-30`; `IProductsService.cs`
- **Description:** no `CancellationToken` is accepted or passed. The body is read into a string and then parsed.
- **Impact:** work continues after the client disconnects. Each response allocates twice.
- **Recommendation:** pass `CancellationToken` from the endpoint down to `HttpClient`, and deserialize from the stream
  with `ReadFromJsonAsync`.
- **Verification:** `GetAll_Cancelled_ThrowsOperationCanceled`; a caller that disconnects gets 499 without a body
  (`ClientAborted_Writes499WithoutBody`).

#### F-10 One log entry per product; interpolated log messages

- **Severity / priority:** Medium / Should · **Category:** Performance efficiency · **Evidence:** Confirmed (analyzers
  CA1848, CA1873, CA2254)
- **Location:** `ProductsService.cs:24-27`
- **Description:** the same message, `Fetched : N number of products`, is logged once for each product, with string
  interpolation.
- **Impact:** N identical log entries per request (about 50 with the original upstream data). Log volume and cost grow
  with the catalogue, and structured fields are lost.
- **Recommendation:** log once with a message template (`{Count}`), using source-generated `LoggerMessage` where hot.

#### F-11 Second DI container built during service registration

- **Severity / priority:** Medium / Should · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Infrastructure/Configuration/DefaultConfiguration.cs:7-8`
- **Description:** `services.BuildServiceProvider()` is called to obtain `IConfiguration`.
- **Impact:** a second container is created and never disposed, so singletons can be instantiated twice. This is a
  known anti-pattern (ASP0000).
- **Recommendation:** pass `IConfiguration` in, or use
  `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()`.
- **Verification:** `MissingBaseUrl_FailsOnResolve`.

#### F-12 Product list fetched without pagination

- **Severity / priority:** Medium / Should · **Category:** Performance efficiency · **Evidence:** Confirmed
- **Location:** `ProductsService.cs:20`
- **Description:** `GET products` is requested without `offset` and `limit`, although the upstream supports them (see
  the Postman request "pagination").
- **Impact:** response size and latency grow with the catalogue, and each request transfers it in full.
- **Recommendation:** expose optional `offset` and `limit`, validate them (`offset >= 0`, `1 <= limit <= 100`), and
  pass them to the upstream.
- **Verification:** `GetAll_SendsOffsetAndLimit`, `GetProducts_NoLimit_UsesDefault20`,
  `GetProducts_LimitOver100_Returns400`.

#### F-13 Logger not flushed; startup failures unlogged; Development overrides ignored

- **Severity / priority:** Medium / Should · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Api/Program.cs:3-4`; `appsettings.Development.json`
- **Description:** the Serilog logger is built by hand and never flushed or disposed, and exceptions during startup are
  not logged. `appsettings.Development.json` configures `Logging:LogLevel`, which Serilog does not read. There is no
  request logging.
- **Impact:** final log entries can be lost on shutdown, startup failures are invisible, and log levels cannot be tuned
  per environment as intended.
- **Recommendation:** use `AddSerilog` with a bootstrap logger, `try/catch/finally` with `Log.CloseAndFlush()`,
  `UseSerilogRequestLogging()`, and overrides under the `Serilog` section.
- **Resolution:** the bootstrap logger, `try/catch/finally` and `Serilog` overrides per environment are in place.
  Inbound requests are logged once by `RequestTimingMiddleware` instead of `UseSerilogRequestLogging()` (ADR-0015).

#### F-14 Transport hardening missing

- **Severity / priority:** Medium / Should · **Category:** Security · **Evidence:** Confirmed
- **Location:** `CSharpApp.Api/Program.cs:22`; `appsettings.json` (`AllowedHosts`)
- **Description:** `UseHttpsRedirection()` is commented out, there is no HSTS, and `AllowedHosts` is `*`.
- **Impact:** plain-HTTP traffic is accepted when the service is exposed directly, and host-header filtering is
  disabled.
- **Recommendation:** enable HSTS and HTTPS redirection where the service terminates TLS, and document TLS termination
  at the reverse proxy for container deployments. Restrict `AllowedHosts` per environment.
- **Resolution:** `AllowedHosts` is `localhost` in the base configuration, an explicit host list in Docker Compose and
  `*` only in Development; an unlisted `Host` gets 400 (`Request_HostHeader_FilteredOutsideDevelopment`). HSTS is sent
  outside Development. HTTPS redirection stays off, because TLS ends at the reverse proxy (ASM-09).

#### F-15 Integration code placed in the Application layer

- **Severity / priority:** Medium / Should · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Application/Products/ProductsService.cs`; `CSharpApp.Infrastructure.csproj` (reference to
  Application)
- **Description:** the outbound HTTP integration, an infrastructure concern, lives in Application. Infrastructure
  references Application only to register that class. The upstream wire DTOs double as the API's response models.
- **Impact:** the application logic cannot be tested without HTTP, the dependency direction is inverted, and upstream
  contract changes leak straight to API clients.
- **Recommendation:** define client interfaces in Core, implement them in Infrastructure, and keep use cases (queries
  and commands) in Application. Document the decision to reuse upstream DTOs as response models, or introduce separate
  response models.

#### F-16 Target framework `net9.0` instead of the required .NET 10

- **Severity / priority:** Medium / Must · **Category:** Portability · **Evidence:** Confirmed
- **Location:** all `*.csproj`
- **Description:** every project targets `net9.0`. The task requires .NET 10. .NET 9 is a standard-term support release,
  and its support window has likely ended (Plausible: 2026-11-10; not checked against the official lifecycle page).
- **Impact:** the solution does not meet a stated requirement and runs on an unsupported runtime.
- **Recommendation:** retarget to `net10.0` (LTS), align the Microsoft.* packages to 10.0.x, and pin the SDK with
  `global.json`.

#### F-17 Package hygiene: duplicate, unused and mismatched references

- **Severity / priority:** Low / Should · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Api.csproj:10-12,17`; `CSharpApp.Application.csproj:10-11`;
  `CSharpApp.Infrastructure.csproj:10-14`
- **Description:** `Microsoft.AspNetCore.OpenApi` is referenced twice (NU1504). Seven packages are referenced but
  unused (section 4.2). `Asp.Versioning.Mvc.ApiExplorer` is the MVC package, while the API uses Minimal APIs
  (`Asp.Versioning.Http` plus `.AddApiExplorer()`), so OpenAPI is not grouped by version.
- **Impact:** larger dependency surface, slower restores, licence exposure if MediatR is upgraded without review.
- **Recommendation:** remove unused references; use `Asp.Versioning.Http`; if MediatR is kept, pin it at 12.5.0 (the
  last Apache-2.0 release) and record why.

#### F-18 RPC-style route and no declared API contract

- **Severity / priority:** Low / Should · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Api/Program.cs:26-32`
- **Description:** the route is `api/v1/getproducts`. There are no `Produces`/`ProducesProblem` declarations and no
  input validation.
- **Impact:** an inconsistent resource model once getOne and create are added, and an incomplete OpenAPI document for
  consumers.
- **Recommendation:** use resource routes (`/api/v1/products`, `/api/v1/products/{id}`), `TypedResults`, endpoint
  metadata and request validation.

#### F-19 Ambiguous setting names and units

- **Severity / priority:** Low / Could · **Category:** Maintainability · **Evidence:** Confirmed
- **Location:** `CSharpApp.Core/Settings/HttpClientSettings.cs`; `RestApiSettings.cs`
- **Description:** `LifeTime: 10` and `SleepDuration: 100` do not state their units. `Username` maps to the upstream
  field `email`. Every property is nullable without validation.
- **Impact:** misconfiguration is easy and is discovered only at runtime.
- **Recommendation:** put units in the names (for example `TotalTimeoutSeconds`), add data-annotation validation with
  `ValidateOnStart`, and document the `Username` → `email` mapping.

#### F-20 Sample JWTs committed in the supplied API collections

- **Severity / priority:** Low / Should · **Category:** Security · **Evidence:** Confirmed (gitleaks v8.30.1, rule `jwt`,
  8 findings)
- **Location:** `postman.json` (lines 465, 1163, 1197); `insomnia.json` (5 occurrences)
- **Description:** the request collections supplied with the task contain hard-coded bearer tokens.
- **Impact:** the tokens belong to the public demo service, which is now offline, so they are of no practical use. The
  pattern is still a risk: committed tokens survive in history, and a secret scanner in CI fails on the base commit.
- **Recommendation:** use collection variables populated by a login request, never literal tokens. Keep the supplied
  files unchanged as client material, and baseline these fingerprints in `.gitleaksignore` with a comment.
- **Resolution:** accepted. The supplied files stay unchanged and history is not rewritten, because the base commit is
  the supplied one. The eight fingerprints are listed with a comment in `.gitleaksignore`, so the gitleaks history and
  working-tree scans in CI pass. If GitHub secret scanning reports the same tokens, they are handled the same way.

## 6. Requirement gaps at the baseline

| Requirement (task statement) | Baseline state | Gap |
|---|---|---|
| Efficient, solid HTTP client approach | Self-created client, no resilience | F-01, F-05, F-09 |
| Products getOne and create | Only getAll exists | Not implemented |
| Categories | DTO only | Not implemented; blocked by F-03 |
| JWT authentication with upstream | Settings only | Not implemented; blocked by F-03 |
| Integration performance measurement via middleware | None | Not implemented |
| Keep the architectural approach | Four layers present | F-15 |
| Unit and integration tests | No test projects | Not implemented |
| Docker support | None | Not implemented |
| CQRS (strong plus) | MediatR referenced, unused | Not implemented |
| Health checks (API and self) | None | Not implemented |
| .NET 10 | `net9.0` | F-16 |
| Error handling, logging, monitoring | Partial | F-07, F-08, F-10, F-13 |
| README with run, test, design and AI usage | Task text only | Not implemented |

## 7. Upstream contract as measured

The contract was taken from the supplied Postman collection and confirmed against the original backend source
(`github.com/PlatziLabs/fake-api-backend`, commit `12bbc93`, last updated 2025-03-11), run locally. The full register
is [`docs/ASSUMPTIONS.md`](ASSUMPTIONS.md).

- Login returns `access_token` and `refresh_token` (ASM-01).
- Access tokens live 20 days and refresh tokens 10 hours (ASM-04).
- Fractional prices are accepted, echoed in the create response and stored as integers (ASM-05).
- An unknown id returns **HTTP 400** with `"name":"EntityNotFoundError"`, not 404 (ASM-10).
- Create requests are validated and content-moderated.
- **Only `auth/profile` requires a token.** Products and categories, including create, are unguarded, so the JWT
  refresh and 401-retry path cannot be exercised against the original service; it is verified against the mock with
  token enforcement switched on and by stubbed tests (ASM-06).
- Third-party catalogue pages describe the authentication as "API key obtained by registration". The backend source
  shows email/password login issuing a JWT, and the source was treated as authoritative.

## 8. Upstream availability and limitations of this assessment

- **The upstream service is offline.** As of 2026-11-20 13:15 UTC:
  - `api.escuelajs.co` still resolves to Heroku, but Heroku answers HTTPS with a TLS `unrecognized name` alert and plain
    HTTP with `404 "No such app"`, so no application is attached to the domain any more.
  - The documentation host `fakeapi.platzi.com` no longer has a DNS record.
  - General connectivity from the review machine was verified (other HTTPS sites respond).
  - No official announcement was found. Public sources show this sequence: the backend repository has had no commits
    since 2025-03-11; the documentation site was last archived with HTTP 200 on 2026-08-11 and reported broken on
    2026-09-29; the last archived successful responses from the API are from April 2026; a third party reported the
    API still up on 2026-10-16; the hosting application was gone by 2026-11-20.
  - The parent domain `escuelajs.co` is registered until 2027-08-24 and its `api` record still points to Heroku. The
    domain did not expire: the service was switched off at the hosting level.
  - Re-checked on 2026-11-23 at 12:54 UTC with the same result: plain HTTP answers `404 "No such app"`, HTTPS fails
    with `unrecognized name`, and `fakeapi.platzi.com` has no DNS record.
- **Live alternatives checked (2026-11-20).** None matches the original contract:
  - DummyJSON: JWT login with refresh and protected routes, but categories have no ids and cannot be created;
  - EverREST: categories with ids and JWT refresh, but no category create, string ids, a price object, and a sign-up
    with email verification;
  - FakeStoreAPI: categories are plain strings, the token has no expiry or refresh, and an unknown id returns 200 with
    an empty body.
- **Consequence.** Running the application, including in Docker, needs a local substitute. The solution ships a small
  mock of the original contract and uses WireMock.Net for fault injection in tests ([ADR-0003](adr/0003-contract-based-upstream-mock.md)).
- **.NET 9 end of support** is stated as Plausible and was not checked against the official lifecycle page.
- **No load testing was performed.** The performance findings rest on code characteristics, not measurements.

## 9. Conclusion

The baseline was not production-ready. A critical defect made the only existing feature fail after its first request,
and high-severity configuration, resilience and data-mapping issues blocked or endangered the requested features. The
third-party service itself is offline, so the solution is demonstrated against a local substitute. All findings could
be fixed within the existing four-layer architecture; their final status is in section 5.1.

## Appendix A. Reproduction artefacts

- **Runtime reproduction:** `ASPNETCORE_ENVIRONMENT=Production dotnet run --project CSharpApp.Api`, with
  `RestApiSettings__BaseUrl` pointing to an unreachable host, then three calls to `GET /api/v1/getproducts`. Call 1
  returned HTTP 500 after about 8 s (connect timeout). Calls 2 and 3 returned HTTP 500 in about 5 ms with
  `InvalidOperationException` (F-01, F-05, F-07).
- **Serialization check:** a standalone program using the original `Product` shape:

  | Input | Result | Finding |
  |---|---|---|
  | Price `19.99` | `JsonException` | F-02 |
  | Price `5` with one image | `Images.Count == 0` | F-06 |
  | Body `null` | `Deserialize` returns null | F-08 |

## Appendix B. AI assistance

An AI coding assistant helped run the analyzers, the dependency audit and the reproductions, produced the independent
second review and drafted this report. Every finding was checked by hand against the code and the reproduction output
listed above.
