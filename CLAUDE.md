# .NET 10 Clean Architecture Template

Aspire-orchestrated Clean Architecture / CQRS / DDD solution over PostgreSQL, with Azure Functions consuming domain events off a Service Bus topic.

This codebase is maintained by AI agents, so it favours **mechanical rules over architectural taste**. The convention tests in `src/StarterApp.Tests/Conventions/` are the authority on structure — read them rather than a prose summary of them. Everything below is the part they *can't* enforce.

## Commands

```bash
dotnet format
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName!~Integration&Category!=Aspire"   # fast: unit + convention + fuzz (its DB-backed handler tests need Docker)
dotnet restore                                                            # locked mode is the default
dotnet restore --force-evaluate                                           # only after an intentional dependency change
dotnet run --project src/StarterApp.AppHost
act                                                                       # run CI locally (flags in .actrc)
```

Commit after every task — uncommitted work is invisible to the other agents sharing this repo. Format, build, and test first, and commit any regenerated `packages.lock.json`.

## Working alongside other sessions

Several agents may hold this checkout at once, so the primary working tree can be sitting on someone else's branch with their uncommitted changes. Don't switch its branch, stage, or commit there. Work in your own worktree instead:

```bash
git fetch origin && git worktree add -b <your-branch> ../<dir> origin/main
```

Push from the worktree and fast-forward `main` from it. Never revert or overwrite changes you didn't make. A doc-only commit in a fresh worktree can't run the build/test pre-commit hook (no build artifacts), so `--no-verify` is acceptable there.

## Rules the compiler and convention tests can't catch

- **Aggregates that override `RecordCreation()` must mint a client-side `Guid.CreateVersion7()` Id in the constructor.** Creation events are captured into the outbox *before* `SaveChanges`, so a database-generated Id doesn't exist yet. Aggregates without creation events may keep int Ids.
- **Don't move order-Id generation inside the EF retry delegate.** `CreateOrderCommandHandler` mints one stable Id before entering the execution strategy and re-checks it on each attempt; a commit-unknown retry would otherwise create a second order and reserve stock twice.
- **One `SaveChangesAsync` per handler, no explicit transaction.** If you need two saves, the aggregate boundary is wrong.
- **Load tracked entities and mutate through domain methods.** `AsNoTracking` + `Update` marks every column modified and loses concurrent writes. `Reconstitute` is test-only rehydration, not a write path.
- **Only by-id queries may be `ICacheable`.** `IDistributedCache` has no pattern-based deletion, so a cached list page is visibly stale after any write. Owner-scoped keys must carry the verified tenant/subject.
- **Owner authorization is application-layer, not endpoint metadata.** Route metadata enforces identity, scope, and MFA before dispatch, but it cannot know a specific row's owner — those checks belong in query predicates and command handlers.
- **Identity is OIDC/JWT, validated in the API itself (zero trust).** `AddJwtBearer` against the configured authority is the only authentication registration. Self-contained JWTs only — never add token-introspection calls to the request path; discovery and JWKS are cached by the bearer handler. Production code reads identity through `ICurrentUser` (populated in exactly one place from validated claims), never `HttpContext.User`, raw claims, or the `Authorization` header outside `Infrastructure/Identity`.
- **Migrations run only through `StarterApp.DbMigrator`** — never at API startup, which races across replicas.
- **Never put `Version=` on a `PackageReference`.** Versions are centralized in `Directory.Packages.props`, and `--force-evaluate` is the only sanctioned way to move a lock file.
- **Never commit a real secret to the tracked tree.** `appsettings.Development.json` is git-ignored with a tracked `.example` template; the `secret-scan` workflow scans full history with a checksum-verified pinned `gitleaks`, and intentional placeholders belong in `.gitleaks.toml` rather than being worked around.
- **Prefer an `.editorconfig` severity entry with a stated reason over a scattered `#pragma`.** Don't mass-apply public-to-internal churn, `ConfigureAwait(false)`, or XML doc comments to satisfy a broad analyzer rule.
- **This is a template: a seam with one implementation is an exemplar, not YAGNI.** `IFeatureToggles`, `IPayloadRedactor`, `IEndpointDefinition`, the cache envelope, and illustrative domain methods exist so a derived project can copy the shape. Simplification passes cut duplication, dead tooling, and hand-rolled BCL; they don't collapse a seam because the second implementation hasn't arrived yet. `docs/DERIVATION-PRUNING.md` is where a derived project decides what to drop. Simplification passes run under the ponytail plugin (`/ponytail`, `/ponytail-audit`), enabled for this repo in `.claude/settings.json`.
- **Prohibited:** AutoMapper (write explicit mappers), MediatR (commercial licence — the custom mediator lives in `Api/Infrastructure/Mediator/`), the repository pattern (DbContext is already unit-of-work plus repository), anemic domain models, public `SetId()`, and code regions or XML doc comments in app code.

## Recorded decisions

Each of these was chosen against a reasonable alternative and carries a **re-add trigger** — the specific fact that would justify revisiting it. Full rationale in [`docs/DECISIONS.md`](docs/DECISIONS.md); don't reverse one without hitting its trigger.

| Decision | Re-add trigger |
|---|---|
| Exceptions are the app-wide error model — no `Result<T>`/ErrorOr | A component with expected, frequent, locally-handled failures may use `Result<T>` internally, never across the mediator/endpoint boundary |
| The domain event *is* the wire contract — no separate `IIntegrationEvent`, no in-process dispatcher | A domain event needs a property the external contract shouldn't carry, or you need a same-transaction in-process reaction |
| Payload capture runs as the *first* middleware, ahead of exception handling and rate limiting | None — moving it behind the rate limiter blinds the audit trail to rejected traffic and needs a new recorded decision |
| AppHost + AppHost.Tests are exempt from `packages.lock.json` | None — the Aspire SDK injects host-RID packages, so no single lock file satisfies locked mode across platforms |
| NuGet signature validation is deliberately off; feed restriction + lock hashes cover it | A clean-cache Linux restore passes across the full package set with `trustedSigners` enabled |
| Reads go through Dapper on a transient `IDbConnection`, not EF Core raw SQL (`SqlQuery<T>`/`FromSql`) | A requirement that *all* SQL flow through EF interceptors/diagnostics, or Dapper blocking a .NET/Npgsql upgrade — converge in one change that also rewrites `DapperConventionTests` |
| Service Bus subscribers get no ordering guarantee (`maxConcurrentCalls: 16`, no sessions) | — subscriber implementations must tolerate out-of-order delivery |
| The `Consistency/` test suite is advisory and human-read; builds never gate on a distance | Two consecutive dated reviews record that no report line informed a finding |

## Where to look

| Task | Source of truth |
|---|---|
| Structural rules of any kind | `src/StarterApp.Tests/Conventions/` |
| Command/query handlers | `.claude/skills/cqrs-patterns/SKILL.md`, pinned exemplars in `docs/exemplars/` |
| Domain models, value objects | `.claude/skills/ddd-implementation/SKILL.md` |
| EF Core config, migrations, Aspire wiring | `.claude/skills/data-access/SKILL.md` |
| Minimal API endpoints | `.claude/skills/api-design/SKILL.md` |
| Tests, FsCheck, convention authoring | `.claude/skills/testing-strategy/SKILL.md` |
| Service Bus emulator, dev tunnels, local CI | `.claude/skills/development-workflow/SKILL.md` |
| Dependencies, custom mediator | `.claude/skills/technology-stack/SKILL.md` |
| Architecture audits | `.claude/skills/architecture-review/SKILL.md`, `docs/ARCHITECTURE_REVIEW.md` (open state), dated records in `docs/reviews/` |
| Outbox / eventing / OIDC identity / payload capture internals | `docs/DECISIONS.md` |
| Replaying a stuck or dead-lettered event | `docs/runbooks/event-replay.md` |
| Recurring async-failure patterns and known defects | `docs/investigations/README.md` |
| Perf gate, security scan | `tests/k6/README.md`, `dast/README.md` |
| Running the stack for the first time (human onboarding) | `docs/GETTING-STARTED.md` |
| Pruning this template into a derived project | `docs/DERIVATION-PRUNING.md` |
| Read-only support SQL | `scripts/reporting/` |
| Reviewer subagents and the branch-review workflow | `.claude/agents/`, `.claude/workflows/architect-review.js` |

A `PreToolUse` hook (`.claude/hooks/protect-commands.sh`) denies catastrophic wipes and prompts on recoverable-but-destructive commands; `permissions.deny` in `.claude/settings.json` blocks reading `.env*`, `appsettings.Development.json`, `secrets/**`, and key material. Both fail open, so neither is a substitute for care.

`AGENTS.md` and the `.agents` tree are generated from the Claude-side docs by `scripts/sync-agent-docs.sh`, which the pre-commit hook runs and stages; CI fails if the committed mirror is stale. Edit the Claude side only, never the mirror.
