# ADR-0002: Code conventions, Central Package Management, analyzers as errors

Status: Accepted\
Date: 2026-11-23

## Context

The task asks for clean, consistent code on .NET 10 under a strict SonarQube-style CI. The base commit targets `net9.0`
(F-16), has duplicate and mismatched package references (F-17) and mixes records and classes with no rule.

## Decision

- DTOs, requests, queries, commands are `sealed record`; handlers, clients, middleware are `sealed class` with a primary
  constructor; settings are `sealed class` with `init` and data annotations.
- `Directory.Build.props`: `net10.0`, C# 14, `Nullable`, `TreatWarningsAsErrors`, `GenerateDocumentationFile`; CS1591 is
  off: XML docs are required on the contract surface only (endpoint records, options, Core interfaces), checked in review.
- Central Package Management with committed `packages.lock.json` (CI and Docker restore `--locked-mode`);
  `SonarAnalyzer.CSharp` and `Meziantou.Analyzer` as global references; every suppression in `.globalconfig` has a reason.

## Alternatives considered

- **Versions per project** (the F-17 mismatches) and **SDK analyzers only** (no Sonar rules of the client's gate).

## Consequences

Every `dotnet build` checks conformity; rules such as MA0048 and S1075 change supplied code. Credentials go to
user-secrets and `.env`, not `appsettings.json` (F-04, ASM-15). `SonarAnalyzer.CSharp` is under the SONAR
Source-Available License v1.0: use as a build analyzer is allowed; it is not redistributed.

## Links

Findings F-04, F-16, F-17; ASM-15.
