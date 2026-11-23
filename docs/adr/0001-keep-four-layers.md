# ADR-0001: Keep the four layers; client interfaces in Core, implementations in Infrastructure


Status: Accepted\
Date: 2026-11-23

## Context

The task asks to keep the existing architecture (Api / Application / Core / Infrastructure). In the base commit the
outbound HTTP code (`ProductsService`) lives in Application, Infrastructure references Application, and the upstream
wire DTOs double as API responses (F-15). Use cases cannot be tested without HTTP; the dependency direction is inverted.

## Decision

- **Core:** DTOs, client interfaces (`IProductsClient`, `ICategoriesClient`, `IAccessTokenProvider`), settings, upstream
  exceptions. **Application:** CQRS handlers on `Mediator` (ADR-0007), depends on Core only.
- **Infrastructure:** typed `HttpClient`s, resilience, token provider, handlers, health check; depends on Core only.
- **Api:** composition root (endpoints, exception handler, middleware, health, rate limiting); one registration class
  per layer. `IProductsService` and `ProductsService` are deleted; the mock in `mocks/` is standalone (ADR-0003).

## Alternatives considered

- **Keep the clients in Application.** Keeps the inverted dependency and HTTP in use cases.
- **One project or vertical slices.** Contradicts the task's "keep the architectural approach".

## Consequences

Handlers are unit-tested against substituted client interfaces (NSubstitute), with no HTTP. No architecture tests:
project references and review hold the layering.

## Links

Findings F-01, F-15; ADR-0003, ADR-0007. A build check shows Infrastructure no longer references Application.
