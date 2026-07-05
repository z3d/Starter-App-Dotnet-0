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

## Comprehensive Multi-Agent Mode (for "thorough"/"comprehensive"/"audit everything" requests)

When the user wants an exhaustive audit (and especially when multi-agent orchestration is opted in), scale Phase 1–2 into a **find → dedup → adversarially verify** workflow via the Workflow tool instead of a single pass. This codebase is mature and hardened, with a long history of *dismissing* plausible "HIGH/CRITICAL" findings as false positives (the current self-assessed score lives in docs/ARCHITECTURE_REVIEW.md — never trust a number embedded in skill text), so the dominant failure mode is **plausible-but-wrong findings, not missed bugs**. Structure the workflow to refute, not to accumulate:

1. **Find** — fan out one finder per subsystem. The eight that map to this repo: security/auth (gateway assertion, scopes, MFA, owner-only policy), CQRS/domain, data-access/persistence, eventing/outbox/Service Bus, payload-capture/PII, build/CI/reproducibility, concurrency/correctness, and convention-test rigor. Tell each finder the bar is high, to ground every finding in `file:line` with quoted evidence, and that **zero findings is an acceptable answer**.
2. **Dedup** — merge by file + normalized title in plain code (a barrier is correct here).
3. **Verify** — for each candidate, spawn **3 independent skeptics with different lenses** (code-truth: re-read the path and its callers; defense-in-depth: is it already covered by a guard/validator/constraint/convention; exploitability: is it actually reachable). Each is instructed to **refute**; keep a finding only on a 2-of-3 majority.

Prior dismissed false positives that recur — do not re-raise without new evidence: the cancel-path "stock double-restore" (blocked by the state machine), `Money.Subtract` negative escape (routes through `Create()`), the AppHost `packages.lock.json` exemption (intentional — Aspire host-RID packages), and "convention tests assert presence not behaviour" (most have already been hardened to IL/SQL behaviour checks).

## Verifying Fixes (do this before marking any finding resolved)

A convention/regression test that always passes is worthless. For every fix, **prove the test fails on the regression it guards**:

1. Inject the exact regression the test should catch (e.g. change `Guid.CreateVersion7()` → `Guid.NewGuid()`, or point a dependency check at a type nothing injects).
2. Run the test — confirm it **fails with the intended message**.
3. Revert the regression and confirm green.

Guard against vacuous passes: any test that filters a discovered set (handlers, aggregates, endpoints) must `Assert.NotEmpty` on that set, so a renamed suffix or moved assembly can't make it silently pass.

**Stale-build caveat:** restoring a mutated source file via `mv`/`cp` of a backup can give it an *older* mtime than the regression-compiled DLL, so MSBuild skips the rebuild and a test looks like it's still failing. After reverting, `touch` the source (or build with `--no-incremental`) before re-running. Don't mistake a stale-DLL failure for a real one.

## Shared Artifact: docs/ARCHITECTURE_REVIEW.md

Read it before starting (it carries the open findings and current score; the dated history and dismissed false positives live in docs/reviews/) and update it after: mark findings resolved with the fix + the regression test added, record dismissed false positives so they aren't re-raised, and adjust the score conservatively. It is the sync point across concurrent agent sessions. Keep CLAUDE.md/AGENTS.md and `.claude/skills`↔`.agents/skills` in sync per the repo's drift rule.

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
- Business rule violations must throw `DomainRuleException` (→ 409) and handler not-found must throw `EntityNotFoundException` (→ 404). Bare BCL `InvalidOperationException`/`KeyNotFoundException` deliberately map to 500: the BCL throws those itself (LINQ `.Single()`, dictionary misses), so treating them as client faults disguises server bugs as 409/404. `ExceptionConventionTests` blocks them from Domain and Application code.
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
- Changing exception-to-status-code mapping (e.g., introducing `DomainRuleException` → 409) will break integration tests that assert on HTTP status codes. Search for `HttpStatusCode.BadRequest` and `HttpStatusCode.InternalServerError` in test files and update assertions.
- Removing DataAnnotation attributes from DTOs will break any tests using `Validator.TryValidateObject()`. These tests should be deleted, not fixed — they test dead validation.

### Presence-vs-Behaviour Convention Gaps (recurring finding class)
- A convention test that asserts a marker/type/interface is *present* often does not prove the *behaviour* happens. Real gaps found this way: injecting `IOwnerOnlyPolicy` without invoking it; injecting `ICacheInvalidator` without calling it; declaring a `Guid` Id without minting it via `Guid.CreateVersion7()`; enforcing only the negative CQRS rule (command handlers must not use `IDbConnection`) without the positive one (they MUST depend on `ApplicationDbContext`).
- The fix pattern is IL/SQL scanning: reuse the existing `ContainsCallToMethod(il, module, name)` helper to scan constructor/method IL (including async state machines) for the required call, and the SQL-literal regex checks for owner-scoped Dapper predicates. Prefer behaviour checks over name/source-text scans.

### Verify Before Trusting the Score
- A high score with "no open findings" is a prompt to verify against code, not to trust prior claims. Each rerun should re-read the actual paths. Most candidate findings on a mature codebase are false positives — the value is in the adversarial verification that filters them, and in the small number of real presence-vs-behaviour gaps that survive.
