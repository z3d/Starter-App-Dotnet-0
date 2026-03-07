---
name: architecture-review
description: Perform a thorough architecture review of a .NET project examining structure, maintainability, clarity, robustness, and goal achievement. Use when the user asks to review, audit, or examine a codebase.
disable-model-invocation: true
user-invocable: true
argument-hint: [project-path]
---

# Architecture Review

Perform a comprehensive architecture review of the project at `$ARGUMENTS` (or the current working directory if no argument provided).

## Review Dimensions

Evaluate the codebase across these five dimensions:

1. **Structure** - Project organization, layer separation, dependency direction, naming conventions
2. **Maintainability** - DRY, consistency, ease of modification, test coverage, convention enforcement
3. **Clarity** - Readability, documentation, dead code, misleading patterns, surprising behavior
4. **Robustness** - Error handling, validation, edge cases, security, data integrity
5. **Goal Achievement** - Does the project accomplish what it sets out to do? Is it fit for purpose?

## Approach

### Phase 1: Parallel Exploration

Launch up to 5 Explore agents in parallel, each focused on a different area:

1. **Project Structure agent**: Solution file, all .csproj files, Directory.Build.props, Program.cs, DI wiring, configuration files
2. **Domain Layer agent**: All entities, value objects, enums, domain events, base classes, validation patterns
3. **Application Layer agent**: Commands, queries, handlers, validators, DTOs, mapping, pipeline behaviors, error handling
4. **Infrastructure Layer agent**: DbContext, entity configurations, repository implementations, migrations, external integrations
5. **API & Tests agent**: Endpoints/controllers, middleware, filters, ALL test files (unit, integration, convention), test infrastructure

Each agent should READ EVERY FILE in its area — do not skim.

### Phase 2: Cross-Cutting Analysis

After exploration, analyze the codebase for issues across all dimensions. Focus on:

- **Exception → HTTP status mapping**: Trace every exception type thrown by handlers through the middleware/global handler to verify correct HTTP status codes
- **Convention test coverage**: Do the convention tests actually catch what they claim to? Check for gaps in assembly scanning scope
- **Validation consistency**: Compare domain guards vs application validators — look for drift or missing coverage
- **Data access patterns**: Verify CQRS boundaries, check for accidental cross-reads (EF in queries, Dapper in commands)
- **Dead code**: Unused attributes, unreachable branches, ghost database columns, no-op methods
- **Logging consistency**: Duplicate logging across layers, missing logs for important paths
- **Security**: Injection vectors, authentication gaps, sensitive data exposure

### Phase 3: Findings Document

Produce findings using this template for each issue:

```
### N. [SHORT TITLE]

**Severity: High|Medium|Low** | Files: `file.cs:line`, `other.cs:line`

[1-3 sentence description of the problem and its impact]

**Fix**: [Concrete, actionable fix — not vague advice]
```

Severity definitions:
- **High**: Bugs, incorrect behavior, security issues, data integrity risks
- **Medium**: Inconsistencies, test gaps, maintainability hazards that will cause issues as the project grows
- **Low**: Clarity improvements, polish, minor optimizations

### Phase 4: Summary

End with:
1. A summary table: `| # | Finding | Severity | Category |`
2. An overall assessment (2-3 sentences)
3. A recommended fix order (highest-impact first)
4. Verification steps (build, test, manual checks)

## Rules

- DO NOT penalize for absent features (outbox, distributed systems, event sourcing, etc.) unless the project claims to support them
- DO NOT recommend adding libraries/frameworks unless there's a concrete problem they solve
- DO recommend removing dead code and unnecessary complexity
- ALWAYS trace exception flows end-to-end — this is the #1 source of bugs in .NET APIs
- ALWAYS check that convention tests actually scan the assemblies they should
- Prefer fixing the root cause over adding workarounds (e.g., fix the global handler rather than adding try-catches everywhere)

## Learnings from Past Reviews

These are real issues found in production .NET projects. Check for each of these specifically:

### Exception Mapping Gaps
- `InvalidOperationException` is commonly thrown for business rule violations (e.g., "cannot delete entity with dependents", "insufficient stock") but often falls through to the default 500 handler. Map it to 409 Conflict.
- Endpoints that catch exceptions locally create inconsistency — some endpoints catch, some don't. Fix the global handler and remove all endpoint-level try-catches.

### Convention Test Blind Spots
- Convention tests that scan only one assembly (e.g., `ApiAssembly`) may miss violations in other assemblies (e.g., `DomainAssembly`). Always verify the scan scope matches the intent.
- Rename tests to reflect their actual scope (e.g., `ApiTypes_MustNotResolveCurrentTimeViaDateTime` not `Types_MustNotResolve...`).

### Dead Code Patterns
- DataAnnotation attributes (`[Required]`, `[StringLength]`, `[Range]`) on DTOs that use a custom `IValidator<T>` framework — these are never evaluated but mislead readers. Remove them AND their corresponding `TryValidateObject()` unit tests.
- Ghost database columns: when a migration drops some columns from a table, check if ALL dead columns were dropped. Dapper queries may silently reference leftover columns via fallbacks.

### Duplicate Logging
- Endpoints and handlers both logging the same operation creates noise. Handlers are the authoritative layer — remove logging from endpoints. Exception: endpoint-level null-check logs (e.g., query returns null, endpoint returns 404) are fine since the handler doesn't log these.

### Value Object Completeness
- Value objects that override `Equals(object)` and `GetHashCode()` should also implement `IEquatable<T>` to avoid boxing in LINQ operations.

### Test Impact of Fixes
- Changing exception-to-status-code mapping (e.g., `InvalidOperationException` from 500 to 409) will break integration tests that assert on HTTP status codes. Search for `HttpStatusCode.BadRequest` and `HttpStatusCode.InternalServerError` in test files and update assertions.
- Removing DataAnnotation attributes from DTOs will break any tests using `Validator.TryValidateObject()`. These tests should be deleted, not fixed — they test dead validation.
