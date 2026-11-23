# ADR-0009: Upstream DTOs reused as API responses (a time-boxed trade-off)

Status: Accepted\
Date: 2026-11-23

## Context

The upstream wire DTOs (`Product`, `Category`) double as the API's response models (F-15), so upstream changes reach our
callers directly. They also had defects: an `int?` price fails on a fractional value (F-02) and a getter-only `Images`
list is never populated (F-06). The exercise is time-boxed at 4–6 hours and the API is a thin proxy.

## Decision

- `Product` and `Category` in Core are both the wire models and the response bodies, fixed in place: `decimal Price`, a
  settable `Images` list, `DateTimeOffset?` timestamps, `Category.Slug`; members the upstream may omit stay nullable.
- Request bodies get their own records with validation (ADR-0008). Responses: camelCase, nulls omitted, decimals unchanged.

## Alternatives considered

- **Separate response models with a mapping layer:** isolates callers from upstream changes, but adds a model and a
  mapping per resource with no behaviour difference today.

## Consequences

- Fewer types and no mapping code; the OpenAPI schema is the upstream shape. An upstream rename changes our public
  contract; separate response models are a future improvement.

## Links

Findings F-02, F-06, F-15. Tests: `GetAll_DeserializesProducts_IncludingImagesAndDecimalPrice`,
`Deserialize_UtcTimestamp_KeepsOffset`, `Response_UsesCamelCase_OmitsNulls`.
