---
name: cqrs-patterns
description: Writing command and query handlers — the CQRS split, single dispatch path, owner scoping, validator coverage. Use when implementing or modifying a handler, command, query, or validator.
user-invocable: false
---

# CQRS Implementation Patterns

Read a real handler before writing one. `docs/exemplars/command-handlers/` and `docs/exemplars/query-handlers/` hold the pinned exemplars the consistency cohorts measure drift against — they are the shape to match, and they stay current in a way a snippet here would not.

## The rules

- **Commands use `ApplicationDbContext` and return DTOs; queries use Dapper on `IDbConnection` and return ReadModels.** Never cross. `CqrsConventionTests` enforces both directions, positively and negatively.
- **No repository layer.** DbContext is already unit-of-work plus repository; the indirection bought nothing.
- **A command declares `ICommand` *and* `IRequest<T>`.** `ICommand` is a bare marker. A query declares only `IQuery<T>`, which already extends `IRequest<T>`. A command with no natural result returns `IRequest<Unit>` — there is no non-generic `IRequest`.
- **Commands:** load tracked entities with `.Include()`, mutate through domain methods, one `SaveChangesAsync(cancellationToken)`, invalidate the cache key *after* the save.
- **Queries:** list columns explicitly (no `SELECT *`), wrap every Dapper read in `PostgresRetryPolicy.ExecuteAsync`, return `null` on a miss rather than throwing.
- **Inject `IOwnerOnlyPolicy` and actually invoke it.** Convention tests IL-scan through async state machines for a real call — injecting without calling fails the build. → [reference/owner-scoping.md](reference/owner-scoping.md)
- **Every command and every query needs an `IValidator<T>`**, including trivial ones. This is deliberate for an agent-maintained codebase: it removes the judgment call about which requests "need" validation. Validators shadow domain guards, so when you change one, check the other — the guard throws as a last defense, the validator returns structured multi-error output for API UX.

Registration is one line — `builder.Services.AddMediator(Assembly.GetExecutingAssembly())` — and discovers every handler.

## Depth

| Topic | Reference |
|---|---|
| Owner scoping: the four convention rules, SQL predicates, create-vs-mutate asymmetry, `OwnerAuthorizationBehavior` | [reference/owner-scoping.md](reference/owner-scoping.md) |
| Mediator interface definitions and the marker-interface pairings | [reference/interfaces.md](reference/interfaces.md) |

## Related skills

- `ddd-implementation` — the aggregates handlers mutate
- `data-access` — EF configuration and migrations behind the command side
- `api-design` — how endpoints dispatch into the mediator
