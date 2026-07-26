---
name: testing-strategy
description: Choosing the right test project, FsCheck property tests, and writing convention tests that catch their regression. Use when writing or modifying tests, or adding a structural rule.
user-invocable: false
---

# Testing Strategy

Two test projects, split by cost:

- **`StarterApp.Tests`** — unit, convention, fuzzing, and API integration. `WebApplicationFactory<IApiMarker>` + Testcontainers PostgreSQL + Respawn per-test reset. In-process: fast, debuggable, Service Bus is a no-op.
- **`StarterApp.AppHost.Tests`** — full distributed app via `DistributedApplicationTestingBuilder`. Only for cross-service paths (API → outbox → Service Bus → Functions). Tag `[Trait("Category", "Aspire")]`; clients via `app.CreateHttpClient("api")`.

Read `Conventions/` before adding a rule and `Fuzzing/` before adding a property — both establish the local idiom.

## The rules

- **Convention tests assert presence *and* behaviour.** Proving a type injects a dependency doesn't prove any code calls it. Use the IL-scan helpers in `ConventionTestBase`; **never hand-roll a raw IL byte loop** — an operand byte read as an opcode silently produces a passing test. → [reference/convention-authoring.md](reference/convention-authoring.md)
- **Never let a discovered set go empty.** Any test filtering types must `Assert.NotEmpty` first, or a renamed suffix turns it into a vacuous pass.
- **Prove a new convention fails before trusting it.** Inject the exact regression, confirm the intended failure message, revert, confirm green. After reverting by file copy, `touch` the source — a stale mtime makes MSBuild skip the rebuild and a stale-DLL failure looks real.
- **Properties are for invariants, not examples.** FsCheck 3.x (`[Property]`, note the 3.x API differs from 2.x) is for laws that hold across all inputs — arithmetic on `Money`, round-trips on stock updates, "valid transitions never throw, invalid ones always do."
- **Consistency reports are advisory.** They land in `docs/_local/consistency-*.txt` per test run; deterministic rules belong in convention tests, and builds never gate on a distance.

## Depth

| Topic | Reference |
|---|---|
| Convention authoring: Best.Conventional built-ins, custom specs, the IL walker, verification discipline | [reference/convention-authoring.md](reference/convention-authoring.md) |

## Related skills

- `architecture-review` — the audit workflow these tests support
- `cqrs-patterns` — the handler rules most conventions encode
