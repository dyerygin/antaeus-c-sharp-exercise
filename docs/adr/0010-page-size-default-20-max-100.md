# ADR-0010: Default page size 20, maximum 100

Status: Accepted\
Date: 2026-11-23

## Context

The base code requested `GET products` with no `offset` or `limit`, so every call transferred the whole catalogue
(F-12). The upstream supports `offset` and `limit` on products and `limit` on categories; with no limit it returns
everything (ASM-01). We set a default and a maximum page size for our list endpoints.

## Decision

- `GET /api/v1/products` takes `offset` (≥ 0, default 0) and `limit` (`[Range(1, 100)]`).
- A missing `limit` uses `PagingSettings.DefaultLimit` = 20; `[Range(1, 100)]` is the single source of the maximum.
- Both values go to the upstream; an out-of-range value returns 400 (ADR-0008) and makes no upstream call.

## Alternatives considered

- **No default, pass-through only:** one request without `limit` still pulls the whole catalogue.
- **A configurable maximum:** two sources of truth. **Cursor paging:** the upstream offers only offset and limit.

## Consequences

- A call without `limit` returns at most 20 items. The response stays a plain JSON array without paging metadata.
- The default changes by configuration; the maximum needs a code change.

## Links

Finding F-12; ASM-01; ADR-0008. Tests: `GetProducts_NoLimit_UsesDefault20`, `GetProducts_LimitOver100_Returns400`,
`GetAll_SendsOffsetAndLimit`.
