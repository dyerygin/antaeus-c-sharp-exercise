# ADR-0007: Source-generated Mediator for CQRS instead of MediatR or a hand-written dispatcher

Status: Accepted\
Date: 2026-11-23

## Context

The task lists CQRS as a strong plus; the exercise owners said no specific library is required. The base commit
references MediatR but does not use it (F-17). MediatR 12.5.0 is the last Apache-2.0 release; 13.0.0 onwards is
commercial. The service is a thin proxy; product create holds the one real piece of logic (ADR-0011).

## Decision

- `Mediator.Abstractions` (Application) and `Mediator.SourceGenerator` (Api) 3.0.2, MIT; `AddMediator` with a scoped
  lifetime. The generator in the Api project discovers the handlers in Application.
- One `sealed record` per operation (`IQuery<T>` or `ICommand<T>`) with its handler nested in it, one file each.
- No pipeline behaviours: validation happens at the endpoint (ADR-0008).

## Alternatives considered

- **MediatR 12.5.0:** pins the last free line. **A hand-written dispatcher:** our own code to test and explain.
- **Endpoints call the clients directly:** simplest, but drops the CQRS the task asks for.

## Consequences

- Handlers are plain classes, unit-tested without HTTP; wiring is generated at build time, so a missing handler is a
  build error. CQRS adds one type per operation for little logic here.

## Links

Finding F-17. Tests: `ProductQueryHandlerTests`, `GetProducts_NoLimit_UsesDefault20`.
