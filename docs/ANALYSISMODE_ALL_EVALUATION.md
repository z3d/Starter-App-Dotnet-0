# AnalysisMode=All Evaluation

Date: 2026-05-24

## Summary

`<AnalysisMode>All</AnalysisMode>` is worth adopting only with an explicit `.editorconfig` severity policy. A strict first build failed with 45 analyzer errors before downstream projects could report. A full inventory pass with warnings not promoted to errors produced 1,334 unique diagnostics across 41 rule IDs.

The adopted policy keeps high-signal, low-churn correctness/clarity rules active, suppresses test-only noise in test projects, and lowers broad library-style or performance rules that conflict with the template's existing Minimal API, CQRS, DTO, and xUnit conventions.

## Inventory And Policy

| Rule | Count | Policy | Reason |
|---|---:|---|---|
| CA1707 | 432 | Suppress tests only | BDD-style underscore test names are the repo convention. |
| CA2007 | 230 | Suppress globally | ASP.NET Core, Functions, and test code intentionally do not use blanket `ConfigureAwait(false)`. |
| CA1515 | 164 | Suppress globally | Broad public-to-internal churn conflicts with API/test discovery and reviewability. |
| CA1307 | 112 | Keep outside tests; suppress tests | Useful in production equality/hash code paths; too noisy for assertion text checks. |
| CA1062 | 94 | Lower to suggestion | Nullable reference types, DI, validators, and convention tests cover most paths; consider focused null-guard hardening later. |
| CA2000 | 33 | Suppress tests only | Current hits are xUnit/Testcontainers/AppHost harness lifetimes. |
| CA1310 | 32 | Keep outside tests; suppress tests | String-comparison clarity is useful in production, noisy in reflection-heavy tests. |
| CA2234 | 31 | Suppress tests only | Test `HttpClient` relative URI calls are readable and covered by configured clients. |
| CA1031 | 27 | Lower to suggestion | Broad catches are intentional in health checks, top-level migrator flow, and test polling; review case-by-case. |
| CA1848 | 20 | Lower to suggestion | Source-generated logging would be a separate logging-performance hardening task. |
| CA1305 | 20 | Lower to suggestion; suppress tests | Culture formatting can matter, but current hits are mostly console/test/log formatting. |
| CA1852 | 18 | Suppress tests only | Reflection-discovered test helper types should not be mechanically sealed. |
| CA1819 | 12 | Suppress globally | Option arrays are intentional framework/serialization shapes; PostgreSQL `xmin` concurrency tokens now use scalar `uint` values. |
| CA1812 | 11 | Suppress tests only | Reflection and generic test stubs are intentionally not directly instantiated. |
| CA1859 | 10 | Lower to suggestion | Concrete-type substitutions can erode dependency-boundary clarity. |
| CA1303 | 9 | Suppress globally | The template is not localized; console/log literals are intentional. |
| CA1873 | 8 | Lower to suggestion | Related to logging-performance hardening, not a correctness rule. |
| CA1869 | 8 | Suppress tests only | Test-local `JsonSerializerOptions` allocations are low risk. |
| CA1826 | 7 | Keep as error; fixed current hits | Low-churn clarity/performance improvement on indexable collections. |
| CA1040 | 6 | Suppress globally | Marker interfaces are core to the custom mediator and API test discovery. |
| CA1002 | 6 | Suppress globally | DTO/options collection shapes are intentionally serializer/binder-friendly. |
| CA1308 | 5 | Suppress globally | Lowercase canonical forms are intentional for headers, entity names, and hashes. |
| CA1032 | 5 | Suppress globally | App exceptions carry domain/API semantics; standard exception constructor churn is low value. |
| CA2227 | 4 | Suppress globally | API DTO collection setters preserve serialization and binding behavior. |
| CA1860 | 4 | Keep as error; fixed current hits | Low-churn collection clarity improvement. |
| CA1034 | 4 | Suppress tests only | Nested test helper types are intentional. |
| CA1806 | 3 | Keep as error; fixed current hits | Unused object creation in throwing tests was easy to clarify with discard assignment. |
| CA1725 | 3 | Keep as error; fixed current hits | Interface parameter-name alignment was harmless and clarifies implementation intent. |
| CA1847 | 2 | Keep as error; fixed current hits | Simple single-character `Contains` cleanup. |
| CA1724 | 2 | Suppress globally | Names like `Mediator` and `Extensions` match established .NET/local conventions. |
| CA1711 | 2 | Suppress globally | `RequestHandlerDelegate` and xUnit collection naming are conventional. |
| CA2263 | 1 | Keep as error; fixed current hit | Generic enum API is clearer. |
| CA2213 | 1 | File-specific suppress | `BoundedCaptureStream` forwards but does not own the ASP.NET response body stream. |
| CA2201 | 1 | Keep as error; fixed current hit | Replaced a generic test-fixture exception with `InvalidOperationException`. |
| CA1861 | 1 | Suppress tests only | One test fixture array allocation is not worth shared mutable fixture state. |
| CA1849 | 1 | Keep as error; fixed current hit | Async data-reader API was a direct improvement. |
| CA1822 | 1 | Keep as error; fixed current hit | Static test helper was trivial and clearer. |
| CA1805 | 1 | Keep as error; fixed current hit | Removed explicit default field initialization. |
| CA1716 | 1 | Suppress globally | `next` is the normal pipeline delegate name in C# middleware/pipeline APIs. |
| CA1056 | 1 | Suppress globally | Options binding keeps `AccountUri` as a nullable string for configuration ergonomics. |
| CA1030 | 1 | Suppress globally | `RaiseDomainEvent` is a DDD method, not a CLR event surface. |

## Changes Made

- Enabled `<AnalysisMode>All</AnalysisMode>` in `Directory.Build.props`.
- Added the curated analyzer severity policy to `.editorconfig`, including test-project suppressions and a file-specific CA2213 suppress for the payload capture stream wrapper.
- Fixed small, low-risk analyzer hits in production and tests: ordinal email hash code, invariant response status formatting, indexer use on indexable collections, generic enum check, discard assignments for expected-throw constructors, async `IsDBNullAsync`, a more specific test fixture exception, static helper method, default field initialization, and cohort parameter naming.
- Updated `AGENTS.md` and `CLAUDE.md` to document the analyzer policy.

## Follow-Up Work

- Revisit CA1062 only as a focused null-guard hardening task; doing it mechanically across handlers, validators, EF configurations, and tests would add a lot of boilerplate.
- Revisit CA1848/CA1873 only if logging allocation/performance becomes a goal; source-generated logging would touch API, Functions, and shared service code.
- Revisit CA1031 catches case-by-case during reliability work rather than with a blanket cleanup.
