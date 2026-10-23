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
cd web && npm install && npm test                                        # the web app's unit and component tests (Vitest); see web/README.md
web/e2e/run-e2e.sh                                                        # the web app in a browser (Playwright) against a stack it boots itself
web/e2e/run-e2e.sh                                                        # the web app in a browser (Playwright) against a stack it boots itself
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

**A worktree ends merged or abandoned, never parked.** When the work is done, fast-forward main and remove the worktree and its branch; when it is dropped, remove them. A branch left for days goes stale against main until nobody can merge it, and a merged worktree left behind hides the parked ones. The `worktree-lifecycle.sh` hook names anything parked, anything merged but not removed and any old stash when a session starts, and at the end blocks once on a worktree this session created and left behind. `worktree-cleanup.sh` runs first at session start and removes what is provably finished: a worktree merged into `origin/main` with no uncommitted or untracked file, and a merged local branch, each untouched for 24 hours (`WORKTREE_CLEANUP=0` turns it off). It never forces, and never touches a remote branch, a locked worktree or the primary checkout.

Push from the worktree and fast-forward `main` from it. Never revert or overwrite changes you didn't make. `.githooks/pre-push` runs the full local gate (format, build, every test project, the web app's checks) before a push leaves, and skips it when every changed path is under `docs/` or ends in `.md`. A doc-only commit in a fresh worktree can't run the build/test pre-commit hook (no build artifacts), so `--no-verify` is accepted there, and only there: the pre-commit hook refuses it on a tree that changes anything else, and refuses `git push --no-verify` outright.

**The machine is shared too.** Disk, processes, ports and the container VM belong to every session on it:

- Check free disk (`df -h`) before a build, an image or a browser stack, and stop under 10 GB: say so and wait for the person.
- Remove the images, containers, tarballs and publish output you made when you are done with them, by name or id.
- Kill only the PIDs you started (keep `$!`, or `lsof -ti :<your port>`), never by name or pattern (`pkill`, `killall`).
- Never stop, reset or recreate the container VM, or prune, reset or `--repair` the engine's shared state, without the person.
- Pick a port nobody is listening on (`lsof -i :<port>`) and say which you took. Before taking the next sequential number (a migration, an ADR), fetch `origin/main` and look in the other worktrees, and take the next one after all of them.

`shared-machine-guard.sh` refuses the kill-by-pattern, VM and prune commands, and builds, image work and browser runs under `AGENT_MIN_FREE_GB` (10) free.

## Running a task

- **Know what done is, and keep going until it is.** A task, and any brief handed to a subagent, says where it finishes (the tests that pass, what is merged) and when to stop. When a step doesn't need the person's input, keep going, and put status notes in the same message as the next action.
- **Stop and ask only when you can't continue without the person, or before anything destructive:** deleting data, force-pushing, deploying, or changing anything outside this repository.
- **A long run keeps its task list in a file**, ticked as it goes: a scratch file, or the repo's status document when the items should outlive the run. A list that lives only in the conversation is lost when the context is summarised.
- **A subagent's finding is checked against the code before it is accepted or passed on.**
- **Say what you couldn't confirm, and where you looked.** A regulatory fact, a root cause or a test you didn't run is marked as unconfirmed, never stated as known.
- **A failing test is a defect until a quiet rerun clears it.** Rerun the failing test alone, with no other build, test run or stack going on the machine; "load", "flaky" or "timing" is a cause only when that rerun passes, and the rerun is the evidence you cite. A failure you could not reproduce still goes in the tracker, with the test, the error and what else was running.
- **CI on main that is red, or never started, is reported before anything else.** `ci-status.sh` names it at session start; when GitHub refused to start the jobs (billing, spending limit) CI is off for the repository and only the person can turn it back on.
- **A defect you notice goes in the repo's tracker before the run ends, whether or not you fix it.** The tracker is the document this repo keeps its features and open work in (named under "Where to look"); a defect fixed in the run is recorded there as done, with what was wrong. One left unfixed says what was seen, on which screen or endpoint, and that the cause is unconfirmed. A finding that lives only in the conversation, a screenshot or a PR is lost when the context is summarised.
- **Never weigh scope on human-team effort.** Agents build this repository, so "an N-month build" or "two sprints" is no reason to cut or defer a feature. Weigh it on what code can't solve: outside dependencies (regulators, banks, auditors, partner APIs and their access terms), evidence of demand, the cost of being wrong, and what it depends on. A research or spec doc that argues from human effort is flagged and discounted.
- **End a run with what is waiting on the person first,** then what changed, then what was found.

## Rules the compiler and convention tests can't catch

- **Aggregates that override `RecordCreation()` must mint a client-side `Guid.CreateVersion7()` Id in the constructor.** Creation events are captured into the outbox *before* `SaveChanges`, so a database-generated Id doesn't exist yet. Aggregates without creation events may keep int Ids.
- **Don't move order-Id generation inside the EF retry delegate.** `CreateOrderCommandHandler` mints one stable Id before entering the execution strategy and re-checks it on each attempt; a commit-unknown retry would otherwise create a second order and reserve stock twice.
- **One `SaveChangesAsync` per handler, no explicit transaction.** If you need two saves, the aggregate boundary is wrong.
- **Load tracked entities and mutate through domain methods.** `AsNoTracking` + `Update` marks every column modified and loses concurrent writes. `Reconstitute` is test-only rehydration, not a write path.
- **Only by-id queries may be `ICacheable`.** `IDistributedCache` has no pattern-based deletion, so a cached list page is visibly stale after any write. Owner-scoped keys must carry the verified tenant/subject.
- **Owner authorization is application-layer, not endpoint metadata.** Route metadata enforces identity, scope, and MFA before dispatch, but it cannot know a specific row's owner — those checks belong in query predicates and command handlers.
- **Identity is OIDC/JWT, validated in the API itself (zero trust).** `AddJwtBearer` against the configured authority is the only authentication registration. Self-contained JWTs only — never add token-introspection calls to the request path; discovery and JWKS are cached by the bearer handler. Production code reads identity through `ICurrentUser` (populated in exactly one place from validated claims), never `HttpContext.User`, raw claims, or the `Authorization` header outside `Infrastructure/Identity`.
- **The web app is the client half of that seam, and `web/src/lib/auth.ts` is the only code that holds the token.** It signs the person in with the authorization code flow and PKCE as a public client, the API client's middleware (`src/lib/api.ts`) sends the token and answers a 401 and the step-up 403, and nothing else reads `sessionStorage` or the `Authorization` header. The API is called only through `web/src/lib/api.ts`'s `client`, `openapi-fetch` typed by route over `web/src/lib/api.generated.ts` (`npm run gen:api` writes it from the running API's OpenAPI document; `web/e2e/run-e2e.sh` regenerates it against the API it boots and CI fails when the committed file differs), never through a hand-typed DTO or a hand-written fetch. A hosted instance learns its identity provider from `/config.js` rendered at start-up, never from a value baked into the bundle.
- **Migrations run only through `StarterApp.DbMigrator`** — never at API startup, which races across replicas — and before the new revision rolls out; until then its `/health/ready` reports the database behind. Each script commits on its own under `lock_timeout` and `statement_timeout`; `-- migrator: no-transaction` as a script's first line, with one statement, is the only way out of a transaction (`CREATE INDEX CONCURRENTLY`).
- **A connection string with no password means the hosting identity's Entra token, everywhere.** The user is
  the one named, or — the shape Aspire emits for an Entra-only server — the principal the token was issued to.
  Take connections from the process `NpgsqlDataSource` (`AddDatabaseDataSource`); a raw `new NpgsqlConnection`
  or `UseNpgsql(string)` is a build error. The migrator resolves the token once via `DatabaseAuthentication.ResolveForDirectUseAsync`.
- **Never put `Version=` or `VersionOverride=` on a `PackageReference`.** Versions are centralized in `Directory.Packages.props` and restore refuses either attribute (NU1008, NU1013); `--force-evaluate` is the only sanctioned way to move a lock file.
- **Build Azure clients through `AzureClientAuthentication`, never `new BlobServiceClient(string)` / `new ServiceBusClient(string)` directly.** The shape of the configured value picks the credential (keyed connection string locally, endpoint + managed identity when deployed), the same rule as `DatabaseAuthentication`.
- **Never commit a real secret to the tracked tree.** `appsettings.Development.json` is git-ignored with a tracked `.example` template; the `secret-scan` workflow scans full history with a checksum-verified pinned `gitleaks`, and intentional placeholders belong in `.gitleaks.toml` rather than being worked around.
- **Don't mass-apply public-to-internal churn, `ConfigureAwait(false)`, or XML doc comments to satisfy a broad analyzer rule.** A rule that is wrong changes severity in `.editorconfig` with a stated reason; `HousekeepingConventionTests` refuses a `#pragma warning disable` or `SuppressMessage` in production code.
- **This is a template: a seam with one implementation is an exemplar, not YAGNI.** `IFeatureToggles`, `IPayloadRedactor`, the cache envelope, and illustrative domain methods show a derived project the shape. Simplification (ponytail is enabled repo-wide) cuts duplication, dead tooling, and hand-rolled BCL, never a seam waiting for its second implementation. That rule is the template's own: a derived project removes the sample and every capability no module uses at its first module (`docs/DERIVATION-PRUNING.md`, enforced by `DerivationConventionTests`), and a seam survives there only as a real boundary or with a module consumer.
- **Nothing reads the clock except through `TimeProvider`.** `BannedSymbols.txt` bans `DateTime`/`DateTimeOffset` `Now`, `UtcNow` and `Today` in production code (RS0030). Aggregates never touch time at all: `DomainEventsInterceptor` stamps `DateCreated`, `LastUpdated` and every pending event's `OccurredOnUtc` from one clock read at save, so a row and its event never disagree. A business time such as `OrderDate` is a constructor argument the handler supplies from its injected `TimeProvider`.
- **Comments: the default is none.** A comment exists only for a trap the code cannot show, and it is one line. No rationale, no history, no restating the line below. Rationale goes in the commit message. An existing comment that fails this is cut when the line it describes changes, in that same commit; never in a sweep, never by reopening a file for it, and never carried as a review finding. Check before committing: `git diff -U0 main | grep -cE "^\+\s*//"` should be near zero.
- **Prohibited:** AutoMapper (write explicit mappers), MediatR (commercial licence — the custom mediator lives in `Api/Infrastructure/Mediator/`), the repository pattern (DbContext is already unit-of-work plus repository), anemic domain models, public `SetId()`, and code regions or XML doc comments in app code.

## Traps

Each of these cost a session, and none shows up in a diff. The traps that bite only in one place live with it and load when its files are touched (a harness that does not load `.claude/rules` reads them by path): the AppHost and image builds in `.claude/rules/aspire-and-containers.md`, and one each in `.claude/rules/editorconfig.md` and `.claude/rules/exemplars.md`.

- **`dotnet format --verify-no-changes` exits 2 on any warning-level analyzer hit**, not only on formatting. A new rule at `warning` severity fails CI with the code untouched. Introduce rules at `suggestion` or `error`, never `warning`.
- **A tree that has never been restored makes every `using` look unnecessary.** With no `obj/project.assets.json` the analysers resolve nothing, so `dotnet format` in a fresh worktree strips directives and inserts casts across the solution, and `--verify-no-changes` fails on thousands of IDE0005 hits. The pre-commit hook restores before it formats; a `dotnet format` run by hand needs `dotnet restore` first, and `git status` afterwards should list only the files you meant to change.
- **A staged rename survives an explicit `git add` of other paths.** `git mv` stages both halves; a later `git add <files>` and `git commit` carries the rename into an unrelated commit. Check `git status` before each commit while a rename is in flight.
- **Never chain a destructive step after a commit with `;`.** A failed `git commit` stops an `&&` chain but not a `;` one, so a trailing `git worktree remove --force` or `rm -rf` runs against uncommitted work. Commit in its own command, confirm the hash, then merge, push and clean up separately. Never run `worktree remove` from inside that worktree.

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
| One OIDC issuer: `Identity:Authority`, plus `Identity:MetadataAddress` when discovery is fetched over another hostname | One API must accept tokens from a second issuer — an `Authentication:Issuers[]` list and a policy scheme that forwards on the token's issuer, never a second `AddJwtBearer` |
| The Entra token is the database credential in every process; a password-less connection string means the hosting identity (the named user, or the token's own principal when none is named), and raw `new NpgsqlConnection` / `UseNpgsql(string)` are banned | A PostgreSQL host with no Entra support (self-hosted) — already handled: a string with a password is used as given; never add a second credential mechanism |

## Where to look

| Task | Source of truth |
|---|---|
| Structural rules of any kind | `tests/StarterApp.Tests/Conventions/` |
| Command/query handlers | `.claude/skills/cqrs-patterns/SKILL.md`, pinned exemplars in `docs/exemplars/` |
| Domain models, value objects | `.claude/skills/ddd-implementation/SKILL.md` |
| EF Core config, migrations, Aspire wiring | `.claude/skills/data-access/SKILL.md` |
| Minimal API endpoints | `.claude/skills/api-design/SKILL.md` |
| Tests, FsCheck, convention authoring, whether a test is worth having | `.claude/skills/testing-strategy/SKILL.md` |
| Adding a skill, or turning a finding or a correction into a test, rule or skill | `.claude/skills/skill-authoring/SKILL.md` |
| Service Bus emulator, dev tunnels, local CI | `.claude/skills/development-workflow/SKILL.md` |
| Dependencies, custom mediator | `.claude/skills/technology-stack/SKILL.md` |
| Architecture audits | `.claude/skills/architecture-review/SKILL.md`, `docs/ARCHITECTURE_REVIEW.md` (open state), dated records in `docs/reviews/` |
| The tracker: features, open work and defects found | `docs/ARCHITECTURE_REVIEW.md` (open state) |
| Outbox / eventing / OIDC identity / payload capture internals | `docs/DECISIONS.md` |
| Replaying a stuck or dead-lettered event | `docs/runbooks/event-replay.md` |
| A scheduled job failed or didn't run; the alert queries | `docs/runbooks/scheduled-jobs.md` |
| Recurring async-failure patterns and known defects | `docs/investigations/README.md` |
| Perf gate, security scan | `tests/k6/README.md`, `dast/README.md` |
| Running the stack for the first time (human onboarding) | `docs/GETTING-STARTED.md` |
| Pruning this template into a derived project | `docs/DERIVATION-PRUNING.md` |
| The web app, the generated API client, the browser tests and the web image | `web/README.md` |
| Read-only support SQL | `scripts/reporting/` |
| Reviewer subagents and the branch-review workflow | `.claude/agents/`, `.claude/workflows/architect-review.js` |

`.githooks/pre-push` runs the full gate before any push, for every harness and for a person. `.claude/hooks/pre-commit.sh` formats, builds and tests before any command that runs `git commit` (also after `cd <dir> &&` or as `git -C <dir> commit`), in the tree the commit lands in. `permissions.deny` in `.claude/settings.json` refuses reading or editing `.env*`, `appsettings.Development.json`, `secrets/**` and key material, and editing a lock file; the harness enforces it for its file tools, not for a shell command that writes the file. Neither is a substitute for care, and nothing prompts before a destructive command.

This file is the one set of instructions for every agent harness. Claude Code (v2.1.277 and later) reads `AGENTS.md` by itself only when no `CLAUDE.md` or `CLAUDE.local.md` sits in the working directory or above it; the one-line `@AGENTS.md` import in `CLAUDE.md` makes it read this file whichever of those exist, and in a session that cannot read `AGENTS.md` directly, and the import never loads it twice. There is no mirror to keep in step; edit this file, `.claude/rules/` and `.claude/skills/` only.
