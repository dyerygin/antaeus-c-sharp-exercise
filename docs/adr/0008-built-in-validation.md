# ADR-0008: Built-in .NET 10 validation instead of FluentValidation

Status: Accepted\
Date: 2026-11-23

## Context

Validation and edge-case tests are a client priority, and the task asks for .NET 10 features in use. The base commit
references FluentValidation but validates no input (F-17, F-18). ASP.NET Core 10 adds built-in Minimal API validation:
`AddValidation()` with DataAnnotations returns an automatic 400 ProblemDetails.

## Decision

- `AddValidation()` with DataAnnotations; FluentValidation is removed. `CreateProductRequest`: `Title` 1–200, `Price`
  0.01–1 000 000, `Description` 1–2000, `CategoryId` ≥ 1, `Images` 1–10 absolute http(s) URLs (`[AbsoluteHttpUrls]`).
- `offset` ≥ 0, `limit` in `[Range(1, 100)]` (the single source of the maximum, ADR-0010), `id` ≥ 1.
- Rules that need the upstream stay in Application: an unknown `categoryId` raises `RequestValidationException`.

## Alternatives considered

- **FluentValidation in a mediator behaviour:** an extra dependency. **Manual checks in handlers:** repetitive.

## Consequences

- Invalid requests return 400 with the field in `errors` and never reach a handler or the upstream.
- DataAnnotations do not recurse into list items, so the URL rule is one attribute over the whole list.

## Links

F-12, F-17, F-18. Tests: `ProductsValidationTests`, `AbsoluteHttpUrlsAttributeTests`,
`CreateProduct_UnknownCategory_Returns400OnCategoryId`.
