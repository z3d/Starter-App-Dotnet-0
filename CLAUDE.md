# .NET 10 Clean Architecture Template

Aspire-orchestrated Clean Architecture / CQRS / DDD solution over PostgreSQL, with Azure Functions consuming domain events off a Service Bus topic.

This codebase is maintained by AI agents, so it favours **mechanical rules over architectural taste**. The convention tests in `tests/StarterApp.Tests/Conventions/` are the authority on structure — read them rather than a prose summary of them. Everything below is the part they *can't* enforce.

## Commands

```bash
dotnet format
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName!~Integration"                  # fast: unit + convention + fuzz (its DB-backed handler tests need Docker)
STARTERAPP_ASPIRE_TESTS=true dotnet test tests/StarterApp.AppHost.Tests  # Aspire end-to-end; skipped unless opted in
dotnet restore                                                            # locked mode is the default
dotnet restore --force-evaluate                                           # only after an intentional dependency change
dotnet run --project dev/StarterApp.AppHost
act                                                                       # run CI locally (flags in .actrc)
```

Commit after every task — uncommitted work is invisible to the other agents sharing this repo. Format, build, and test first, and commit any regenerated `packages.lock.json`.

Every agent commit ends with `Harness:` and `Model:` trailers naming the tool and the model(s) that produced it (comma-separate several), after any attribution the harness adds itself:

```
Harness: Claude Code
Model: claude-fable-5-1
```

`.githooks/commit-msg` rejects an agent commit without them (it recognises the harness from its environment and fails open for a human), and `dotnet build` points `core.hooksPath` at `.githooks` so a fresh clone or worktree needs no setup.

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
- **A connection string with no password means the hosting identity's Entra token, everywhere.** The user is
  the one named, or — the shape Aspire emits for an Entra-only server — the principal the token was issued to.
  Take connections from the process `NpgsqlDataSource` (`AddDatabaseDataSource`); a raw `new NpgsqlConnection`
  or `UseNpgsql(string)` is a build error. The migrator resolves the token once via `DatabaseAuthentication.ResolveForDirectUseAsync`.
- **Never put `Version=` on a `PackageReference`.** Versions are centralized in `Directory.Packages.props`, and `--force-evaluate` is the only sanctioned way to move a lock file.
- **Build Azure clients through `AzureClientAuthentication`, never `new BlobServiceClient(string)` / `new ServiceBusClient(string)` directly.** The shape of the configured value picks the credential (keyed connection string locally, endpoint + managed identity when deployed), the same rule as `DatabaseAuthentication`.
- **Never commit a real secret to the tracked tree.** `appsettings.Development.json` is git-ignored with a tracked `.example` template; the `secret-scan` workflow scans full history with a checksum-verified pinned `gitleaks`, and intentional placeholders belong in `.gitleaks.toml` rather than being worked around.
- **Prefer an `.editorconfig` severity entry with a stated reason over a scattered `#pragma`.** Don't mass-apply public-to-internal churn, `ConfigureAwait(false)`, or XML doc comments to satisfy a broad analyzer rule.
- **This is a template: a seam with one implementation is an exemplar, not YAGNI.** `IFeatureToggles`, `IPayloadRedactor`, the cache envelope, and illustrative domain methods show a derived project the shape. Simplification (ponytail is enabled repo-wide) cuts duplication, dead tooling, and hand-rolled BCL, never a seam waiting for its second implementation. That rule is the template's own: a derived project removes the sample and every capability no module uses at its first module (`docs/DERIVATION-PRUNING.md`, enforced by `DerivationConventionTests`), and a seam survives there only as a real boundary or with a module consumer.
- **Nothing reads the clock except through `TimeProvider`.** `BannedSymbols.txt` bans `DateTime`/`DateTimeOffset` `Now`, `UtcNow` and `Today` in production code (RS0030). Aggregates never touch time at all: `DomainEventsInterceptor` stamps `DateCreated`, `LastUpdated` and every pending event's `OccurredOnUtc` from one clock read at save, so a row and its event never disagree. A business time such as `OrderDate` is a constructor argument the handler supplies from its injected `TimeProvider`.
- **Prohibited:** AutoMapper (write explicit mappers), MediatR (commercial licence — the custom mediator lives in `Api/Infrastructure/Mediator/`), the repository pattern (DbContext is already unit-of-work plus repository), anemic domain models, public `SetId()`, and code regions or XML doc comments in app code.

## Traps

Each of these cost a session, and none shows up in a diff.

- **xUnit builds the collection fixture even when every fact in the collection is skipped.** An `[AspireFact]` skip alone still boots the distributed app. `AspireE2EFixture.InitializeAsync` returns early when `STARTERAPP_ASPIRE_TESTS` is unset; keep that guard if the fixture is rewritten.
- **`dotnet format --verify-no-changes` exits 2 on any warning-level analyzer hit**, not only on formatting. A new rule at `warning` severity fails CI with the code untouched. Introduce rules at `suggestion` or `error`, never `warning`.
- **Container image builds on a CRLF working tree fail formatting inside the image.** `EnforceCodeStyleInBuild` runs in the Dockerfile's build stage. Pass `-p:EnforceCodeStyleInBuild=false` to the in-container build or normalise line endings first.
- **The Service Bus emulator exits 139 when several worktree stacks are running.** Each worktree's AppHost creates its own persistent container set; at around eight the emulator segfaults and the outbox and Functions facts fail for reasons unrelated to the change. Remove stale sets (`podman ps -a`, filter by the AppHost hash suffix) before an Aspire run.
- **A staged rename survives an explicit `git add` of other paths.** `git mv` stages both halves; a later `git add <files>` and `git commit` carries the rename into an unrelated commit. Check `git status` before each commit while a rename is in flight.
- **Never chain a destructive step after a commit with `;`.** A failed `git commit` stops an `&&` chain but not a `;` one, so a trailing `git worktree remove --force` or `rm -rf` runs against uncommitted work. Commit in its own command, confirm the hash, then merge, push and clean up separately. Never run `worktree remove` from inside that worktree.
- **`.editorconfig` sections written as `**/*.cs` do not match files at the directory root.** `[src/X/**/*.cs]` misses `src/X/Program.cs`; add a sibling `[src/X/*.cs]` section, as the test-project sections do.
- **Adding a constructor dependency to every handler moves the consistency fingerprints.** `ConstructorDependencyCount` changes and `ExemplarAlignment_DocumentedDependencyCountsMatchCode` fails until the counts in `docs/exemplars/*/README.md` are updated to match.

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
| Dapper reads retry through the hand-rolled `PostgresRetryPolicy` with a total delay budget, not Polly | Polly gains a total-delay budget, or the read path needs a second resilience concern (breaker, hedging) |
| The `Consistency/` test suite is advisory and human-read; builds never gate on a distance | Two consecutive dated reviews record that no report line informed a finding |
| Managed identity everywhere when published: Seq is not published, Redis becomes Azure Managed Redis (keys disabled), Keycloak's admin password is a generated secret | None — a dependency that cannot take a managed identity is replaced or dropped, never given a key |
| One OIDC issuer: `Identity:Authority`, plus `Identity:MetadataAddress` when discovery is fetched over another hostname | One API must accept tokens from a second issuer — take Gumnut's `Authentication:Issuers[]` + policy-scheme shape whole, never a second `AddJwtBearer` |
| The Entra token is the database credential in every process; a password-less connection string means the hosting identity (the named user, or the token's own principal when none is named), and raw `new NpgsqlConnection` / `UseNpgsql(string)` are banned | A PostgreSQL host with no Entra support (self-hosted) — already handled: a string with a password is used as given; never add a second credential mechanism |

## Where to look

| Task | Source of truth |
|---|---|
| Structural rules of any kind | `tests/StarterApp.Tests/Conventions/` |
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

Two `PreToolUse` hooks enforce what an instruction can only ask for: `.claude/hooks/protect-commands.sh` denies catastrophic wipes and prompts on recoverable-but-destructive commands, and `.claude/hooks/guard-edits.sh` refuses a `Version=` on a `PackageReference`, a hand-edited lock file and any write to a secrets file; `permissions.deny` in `.claude/settings.json` blocks reading `.env*`, `appsettings.Development.json`, `secrets/**`, and key material. All fail open, so none is a substitute for care.

`AGENTS.md` is a pointer to this file, so other agent harnesses read the same instructions. There is no mirror to keep in step; edit this file and `.claude/skills/` only.
