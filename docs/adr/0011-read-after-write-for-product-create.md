# ADR-0011: Read-after-write for product create (products only)

Status: Accepted\
Date: 2026-11-23

## Context

On `POST products` the upstream echoes a fractional price (`10.5`) but stores and later returns an integer (`10`)
(ASM-05). The base code's `int? Price` made such a create return 500 although the product was created (F-02); with
`decimal` the echo deserializes but differs from the stored data. The response should show the stored data.

## Decision

- The `CreateProductCommand` handler calls `CreateAsync`, then `GetByIdAsync(created.Id)` on its own timeout
  (`TotalTimeoutSeconds`, not the request token), and returns the stored product with 201 and `Location`. If the
  read-back fails, it returns the echo and logs a Warning: the create already happened, so the request must not fail.
- An unknown `categoryId` (ASM-10) becomes `RequestValidationException`, so the caller gets 400 on that field.

## Alternatives considered

- **Return the create echo:** the caller sees `10.5`, later reads show `10`. **Truncate ourselves:** copies a quirk.

## Consequences

- Each product create makes two upstream calls. When the read-back fails the response can carry the echoed price;
  the Warning log is the signal.

## Links

F-02; ASM-05, ASM-10. Tests: `CreateProduct_FractionalPrice_ReturnsStoredPrice`,
`ReadBack_UsesOwnTimeout_NotRequestToken`, `CreateProduct_ReadBackFails_ReturnsEchoAndLogsWarning`.
