# Architecture Review

This file holds the **current state only**: what is open, what has been accepted with a trigger,
and what is deferred. Every finding's evidence, fix, and regression test lives in the dated record
that produced it. Before dismissing or re-raising anything, check the record, not this file.

| Record | Covers |
|---|---|
| [2026-06 archive](reviews/ARCHITECTURE_REVIEW-2026-06-archive.md) | All session notes, resolved findings, and dismissed false positives through 2026-06-12, plus the July 2026 closures |
| [2026-06-10 codebase review](reviews/REVIEW-2026-06-10.md) | Four-reviewer full-solution pass; all findings resolved the same day |
| [2026-08-04 post-IdP](reviews/ARCHITECTURE_REVIEW-2026-08-04-post-idp.md) | Four findings from the gateway → OIDC conversion; resolved 2026-09-03 |
| [2026-08-04 runtime hardening](reviews/ARCHITECTURE_REVIEW-2026-08-04-runtime-hardening.md) | Five findings; resolved 2026-09-03 |
| [2026-09-02 whole solution](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md) | Twenty-two findings, three dismissed; resolved and validated 2026-09-03 |
| [2026-09-05 targeted review](reviews/ARCHITECTURE_REVIEW-2026-09-05.md) | Eight findings, **all resolved 2026-09-06**; regression and validation evidence in the record |
| [2026-09-06 fix review](reviews/ARCHITECTURE_REVIEW-2026-09-06.md) | Independent review of the 2026-09-06 fix commit; fourteen residuals, twelve fixed the same day, two recorded with triggers |
| [2026-09-08 consistency and simplification](reviews/REVIEW-2026-09-08-consistency-and-simplification.md) | Consistency-suite assessment against its design post (three drifts, fixed the same day) and the template-weighed simplification backlog |
| [2026-09-12 identity security review](reviews/SECURITY-REVIEW-2026-09-12-identity.md) | Manual read plus the security-auditor subagent over the JWT path; five findings fixed the same day (owner check now gates the write, fallback authorization policy, wider claims scan, two-tenant tests, pinned algorithms), five noted |
| [2026-09-27 fresh-eyes review](reviews/REVIEW-2026-09-27-fresh-eyes.md) | Whole solution, reviewers kept blind to prior records; four fixed the same day, one tried and reverted (limiter order); the follow-up fixed nine more, accepted the probes as gateway-protected, and found one finding half false; none open |

## Overview

A .NET 10 Clean Architecture template (CQRS, DDD, transactional outbox, Aspire) over a deliberately
small e-commerce domain. It is agent-maintained, so its weight is a design stance: patterns are the
pedagogy and convention tests are the product. A 2026-06-12 complexity review confirmed that stance
and pruned what failed it; its backlog was fully delivered and is preserved in the git history of
the retired `docs/ROADMAP.md`.

**Score: 8.0/10, last set 2026-09-27.** The score is self-assessed by the maintaining agents with
no external validator and no fixed rubric; treat it as a maintenance log, not an audit. It dips on
discovery and recovers only with verified fixes. The 2026-09-27 fresh-eyes review found two High
defects on hot paths (outbox throughput, blob round trips) that earlier reviews had missed; both are
fixed with regressions, but three Medium findings stay open, so it moved from 8.1 to 8.0. The 2026-09-05 review found eight defects, two of
them reopening earlier closures whose tests had checked configuration shape rather than behaviour;
all eight were fixed on 2026-09-06 with failing-before regressions and an independent re-read of
the diff, so the net move from 8.0 is +0.1 rather than a full recovery. Verifiable snapshot as of
2026-09-06 (re-verify, don't trust): 9 command handlers, 7 query handlers, every request validated
by convention, roughly 770 tests green plus the AppHost suite, 39 deterministic DAST runner
regressions in the DAST workflow, nightly k6 gate and DAST scan passing on `main`.

## Strengths

Convention-enforced boundaries (110+ mechanical rules including supply chain and
event coverage); rich aggregates with client-generated v7 ids where creation events need them;
strict CQRS (EF commands, Dapper reads); transactional outbox with claim/salvage, per-cause retry
budgets, a replay verb and runbook, and pinned event-contract snapshots; full payload capture and
audit with per-channel failure policy and owner-scoped redaction; zero-trust OIDC/JWT validated in
the API (asymmetric JWKS, no shared secrets, no bypass mode); owner scoping enforced in
predicates, policy, cache keys, and rate-limit partitions, with policy invocation structurally
verified in the mediator pipeline; refresh-ahead caching with serve-stale-on-error; job-run
history; incident knowledge base and reporting pack, both schema-guarded; supply-chain hardening
(central package management, locked restore, digest-pinned images, SHA-pinned actions, gitleaks,
Dependabot, CodeQL). The full analysis is in the archive.

## Open findings

The eight findings from the [2026-09-05 review](reviews/ARCHITECTURE_REVIEW-2026-09-05.md#resolution--2026-09-06)
are resolved, with failing-before/passing-after regressions and final validation in the record.

Open from the [2026-09-27 review](reviews/REVIEW-2026-09-27-fresh-eyes.md#follow-up-2026-09-27) (the rest were fixed or accepted the same day; see the follow-up table):

- None: every finding is fixed, accepted (#10, probes behind the gateway), or dismissed (#5, #15's gitleaks half).

- **FIXED 2026-10-23: the image scan failed on the .NET 10.0.12 runtime.** CI's `docker-build` job had failed at "Scan migrator image" on every push since the 10.0.13 servicing release (13 Oct): five HIGH CVEs in `Microsoft.NETCore.App.Runtime` (CVE-2026-58280, -69651, -96225, -96231, -96233), and the API and Functions bases also carried seven in ASP.NET Core 10.0.12 (two CRITICAL), and the Functions base 111 fixed `linux-libc-dev` CVEs, none reported because the job stopped at the first scan. Every base image now pins its 10.0.13 digest and `global.json` the 10.0.402 SDK; a local Trivy scan of all four images reports no CRITICAL or HIGH.
- **FIXED 2026-10-23: CI built the web image from a path that no longer exists.** `33e1a23` (20 Oct) moved `web/apps/web/Dockerfile` to `web/Dockerfile` but the `docker-build` job and `.dockerignore`'s `dist/` line kept the old path; the failing migrator scan hid it. Both now name `web/`.
- **FIXED 2026-10-23: the Aspire job trusted no dev certificate once `global.json` named an SDK the runner lacks.** Its trust step ran `dotnet dev-certs https --trust || true` before the SDK was installed, on the runner's preinstalled 10.0.112; with the floor at 10.0.402 the host refused to run ("A compatible .NET SDK was not found"), `|| true` hid it, and all five Aspire tests timed out on `/health/ready` with `UntrustedRoot` (run 39838839967). The step now runs after the shared SDK setup.

- **OPEN — bearer tokens are not sender-constrained.** The retired gateway assertion was bound to
  method and path with a ~150s lifetime; an IdP bearer token is valid for any endpoint in its
  audience until expiry. Mitigated by short lifetimes and strict audience validation. Fix: DPoP
  (RFC 9449) or mTLS-bound tokens (RFC 8705); Keycloak supports both locally, Entra's narrower
  support constrains the production IdP choice. Trigger: before any production deployment on an
  untrusted network, or the first token observed outside its intended client.

Open from the 23 October 2026 audit of a project derived from this template, each checked against this repository's code before it went in (severity is the one checked here):

- **DONE 2026-10-23 (the local half; the ruleset is the owner's) — nothing on the server gates a push to `main`.** The repository is public, so branch protection and rulesets are available, but neither is set (`gh api repos/z3d/Starter-App-Dotnet-0/branches/main/protection` answers 404 "Branch not protected"; `/rulesets` is `[]`). CI runs and is green, but only after the push lands. The local gate is `.claude/hooks/pre-commit.sh`, wired for Claude Code alone (`.claude/settings.json:51`; `.githooks` holds only `commit-msg`), which exits on `-n`/`--no-verify` without checking that the commit is doc-only (`pre-commit.sh:22`) and runs only `tests/StarterApp.Tests` (`:54,66`): not the AppHost tests, the web app's tests or browser suite, or the image builds. Fix: a ruleset on `main` requiring the CI checks. Done locally: `.githooks/pre-push` runs format, build, every test project and the web app's checks before any push, skipping only a push whose every path is under `docs/` or `*.md`; the pre-commit hook's `--no-verify` path checks the same thing and `git push --no-verify` is refused (`HookScriptTests`, DECISIONS "The local gate runs before a push"). The ruleset waits on the owner.
- **DONE 2026-10-23 — the published migrator runs after the new build.** In publish mode the migrator is a manual Container Apps job that "the hosting repository starts … after each deploy" (`dev/StarterApp.AppHost/Program.cs:186-189`, `docs/GETTING-STARTED.md:142`), while `docs/GETTING-STARTED.md:76` and `docs/DECISIONS.md:354` (expand/contract) say it runs before the new build starts. `WaitForCompletion(migrator)` (`Program.cs:210`) has no effect once published, so a release whose code reads a new column serves errors until the job is run. Unconfirmed: what order a hosting repository actually uses. Fixed: the AppHost comment and GETTING-STARTED say the deployer runs the job to success before rolling out the new revisions, and the API's `database-schema` readiness check reports not-ready until the journal holds every script the build embeds (`DatabaseSchemaHealthCheckTests`; DECISIONS "The migrator runs before the new revision").
- **DONE 2026-10-23 — every pending migration runs in one transaction with no lock or statement timeout.** `DatabaseMigrationEngine.cs:33` uses `.WithTransaction()`, so all pending scripts share one transaction, and nothing sets `lock_timeout` or `statement_timeout`; a script that waits on a lock held by live traffic waits indefinitely while holding every lock taken so far, and `CREATE INDEX CONCURRENTLY` cannot run in this mode. Grows with table size in a derived project. Fixed: a transaction per script, `Migrator:LockTimeout` (5s) and `Migrator:StatementTimeout` (15min) on the session, and a `-- migrator: no-transaction` header for a one-statement script such as `CREATE INDEX CONCURRENTLY` (`MigrationTransactionTests`, `MigrationCompatibilityConventionTests`).
- **DONE 2026-10-23 (the defaults; matching stays by name) — payload redaction for the log matches names only.** Every captured body goes to the log at Information, redacted (`PayloadCaptureSink.cs:114-126`), and the redactor masks a property only when its name contains an entry of `DefaultSensitivePropertyNames` (`PayloadCaptureOptions.cs:77-95`); free text is masked only where it looks like an email (`JsonPayloadRedactor.cs:97-100`). The list has `phone` but not `mobile`, and nothing for income or salary, so any domain field a derived project adds with such a name, or any number, reaches the console, file and OpenTelemetry logs in clear unless the project extends the list. The sample domain has no such field. Fixed: the defaults add `mobile`, `income`, `salary`, `dob`, `birth`, `bsb`, `accountnumber`, `accountno` and `bankaccount` (not a bare `account`, which stopped `accountId` being indexed as an entity reference), and a test pins that `PayloadCapture:SensitivePropertyNames` from configuration adds to the defaults rather than replacing them.
- **DONE 2026-10-23 — the tenant-predicate convention checks each SQL string once, not each SELECT.** `CqrsConventionTests.cs:101-109` passes a literal when `owner_subject = @OwnerSubject` and `tenant_id = @TenantId` appear anywhere in it, so a second SELECT, a UNION branch or a sub-select in the same string can drop the predicate unseen; there is no row-level security behind it here. Fixed: `SqlSelectBlocks` splits a string into its statements, set-operation branches, CTE bodies and subqueries, and each SELECT over an owned table carries both predicates or is a subquery correlated to an enclosing SELECT that does.
- **DONE 2026-10-23 — CodeQL analyses C# only** (`.github/workflows/codeql.yml:38,44`), not the web app's TypeScript (`web/`). Fixed: a language matrix with `javascript-typescript`, and `SupplyChainConventionTests` fails a shipped language the workflow does not name.
- **DONE 2026-10-23 — the scheduled-job alert queries target Application Insights, which nothing sends to.** `docs/runbooks/scheduled-jobs.md:24,31-67` query Application Insights `traces`; the services export OTLP only (`src/StarterApp.ServiceDefaults/Extensions.cs:65-69`) and nothing sets `APPLICATIONINSIGHTS_CONNECTION_STRING` (only `host.json` mentions Application Insights, for sampling). Rewrite against the console-log table the hosting environment collects, or wire Application Insights. Fixed: the queries read `ContainerAppConsoleLogs_CL` (DECISIONS "Scheduled-job alerts query the console logs").
- **DONE 2026-10-24 (recorded, no change; owner) — the payload archive keeps request bodies whole.** Redaction applies to the log only; the archive store gets the body as sent, so a government identifier, a tax file number or anything else a retention or de-identification rule must reach stays in the archive for its retention period, where erasing the person never touches it. The sample domain takes no such field, so the template does not mask the archive. A derived project that takes one decides for itself whether its archive keeps bodies whole or masks them with the same redactor as the log (a setting such as `PayloadCapture:ArchiveBodies: Redacted`, losing the archive's free text), records the choice in its own decisions, and makes the archive's retention policy match. Re-add trigger: the template's own domain takes such a field.
- **DONE 2026-10-23 (recorded, no change) — the rate-limit, cache and audit keys use the raw `tid` claim, not a resolved tenant.** The rate limiter's partition (`ServiceCollectionExtensions.cs:208-214`), the owner cache key (`OwnerScopedCacheKey.cs:10`) and the payload archive's `tenantId` (`PayloadCaptureMiddleware.cs:166-170`) all take `ICurrentUser.TenantId` from the token. Correct while every caller is a person with `tid`; a derived project that adds a service caller naming its tenant some other way (a header) puts every tenant's calls in one bucket and one cache key and records an empty tenant. No such caller exists here. Recorded: `ICurrentUser.TenantId`, set only in `JwtIdentityMiddleware`, is the template's one tenant seam; a caller named another way is resolved there, never in a consumer (DECISIONS "The tenant in keys and logs").
- **DONE 2026-10-23 — logs carry no tenant.** Only `CorrelationId` is pushed to the log context (`PayloadCaptureMiddleware.cs:64`), so an incident cannot be filtered to one tenant. Fixed: `JwtIdentityMiddleware` pushes `TenantId` for the rest of the request.
- **DONE 2026-10-23 — no hook is tested.** Nothing runs `.claude/hooks/pre-commit.sh`, `shared-machine-guard.sh`, `ci-status.sh` or the worktree hooks against a sample payload; a regression in one is found only when it fails to fire. Fixed: `HookScriptTests` runs the guard, pre-commit, pre-push and `ci-status.sh` against sample payloads in a scratch repository (the worktree hooks are still untested).
- **DONE 2026-10-23 — `shared-machine-guard.sh` matches command text by regex.** The heavy-work list (`shared-machine-guard.sh:33`) names `npm ci|install|test` and `npx playwright` only, so `npm run build`, `npm run e2e` (Playwright, `web/package.json:18`) and `pnpm --dir web exec playwright` skip the free-disk check; and the kill pattern (`:15,22`) matches `pkill` after any `;`, so a commit message containing `; pkill` is refused. Fixed: quoted strings and heredoc bodies are emptied before matching (`strip-quoted.py`), and any npm, pnpm, yarn or npx command that installs, tests, builds or runs Playwright is heavy work.
- **DONE 2026-10-23 — `ci-status.sh` reads only the newest failed run's reason.** It looks up GitHub's refusal text for the first failed workflow alone (`ci-status.sh:38-39`) and then reports every listed failure as "GitHub never started the jobs" (`:43-44`), so a real test failure next to a billing refusal is misreported as billing. Fixed: each failed run's own view decides its line.
- **DONE 2026-10-23 — the guard let a suffixed script name past the disk check.** The widened heavy-work pattern wanted the verb followed by a space or the end, so `npm run e2e:stack` (which boots the whole stack here) and `build:mock` skipped it. Found in paperbark's translation. Fixed: a `:` after the verb counts too (`Guard_ChecksFreeDiskBeforeHeavyWork`).
- **DONE 2026-10-23 — the per-SELECT tenant check read a schema name as an outer alias.** `SqlSelectBlocks` counted a subquery as correlated when its text held `<alias>.`, and a schema-qualified table (`orders.order_items`, or agentdesk's `contacts.notes` under an outer `contacts`) matched, so an unscoped subquery passed. Found in agentdesk's merge. Fixed: table references are removed before the correlation test (`TheTenantPredicateCheck_ReadsEachSelectInAString`).
- **DONE 2026-10-23 — a git hook's environment leaked into the gate's tests.** The first push through `.githooks/pre-push` ran `HookScriptTests` with the `GIT_DIR` git exports to hooks, so a scratch repository's `git init` reinitialised the real one and set `core.bare=true` in its shared config, which broke every checkout of it until it was set back. Fixed: the tests drop inherited `GIT_*` variables and the hook clears them before the gate.

From the owner, 23 October 2026:

- **DONE 2026-10-23 — the licence said two things.** `LICENSE` said MIT while `web/package.json` said `AGPL-3.0-only`. The owner chose AGPL-3.0-only on 23 October 2026, for this template and every project derived from it: `LICENSE` is now the unmodified GNU AGPL v3 text from gnu.org, and the README says `AGPL-3.0-only`.

### Found by the review of the 20–23 October commits (23 October 2026)

Read-only review of `b50c629..c6e6856` at c6e6856; each item was read at HEAD, and the three guard items were reproduced by piping the command through the hook with `AGENT_MIN_FREE_GB` forced. Every one is also in the four derived projects, which carry these files unchanged.

- **FIXED 2026-10-23: the shared-machine guard read nothing inside quotes.** `shared-machine-guard.sh:14` empties every quoted string before matching (8b8c7c7), so `bash -c "pkill -f vite"`, `sh -c 'pkill vite'` and `eval "docker system prune -af"` exit 0 where the bare command exits 2. `strip-quoted.py:7` also pairs a stray apostrophe across arguments (`echo "a'b" ; pkill foo ; echo "c'd"` collapses to `echo ""`). `HookScriptTests` covers only bare commands. Fix: match the refused patterns against the raw command as well as the stripped one.
- **FIXED 2026-10-23: `dotnet run`, `dotnet watch` and a bare `yarn` were not heavy work.** `shared-machine-guard.sh:35` lists `build|test|publish|pack|restore` (78482ba); `dotnet run --project dev/StarterApp.AppHost` compiles and boots every container and passes the free-disk check (exit 0 with the floor at 1,000,000 GB), against AGENTS.md's "builds, image work and browser runs under 10 GB free" are refused.
- **FIXED 2026-10-23: CI's failure artifact left out the API log 674c51b moved to keep.** `web/e2e/run-e2e.sh:47-48` writes `api.log` and `vite.log` to `web/e2e-logs/`; `ci.yml:143-150` uploads only `web/playwright-report` and `web/test-results`. A `web-e2e` job that fails inside the API leaves no API log, the loss the commit fixed locally.
- **FIXED 2026-10-23: `doc-only` passed files the tests validate.** `.githooks/doc-only:7` treats everything under `docs/` as documentation, but `KnowledgeBaseConventionTests` parses `docs/investigations/**/knowledge-base.json` and `CohortGovernanceTestBase` reads `docs/exemplars/*/README.md`; a knowledge-base edit that drops `fixCommit` passes `--no-verify` and the pre-push gate and fails first in CI.
- **FIXED 2026-10-23: `dotnet dev-certs https --trust || true` was still masked** (`ci.yml:181`); 0130c70 fixed the ordering but the next SDK-floor bump that outruns the runner recurs as five Aspire timeouts with no failing step.
- **FIXED 2026-10-23: the mediator's instant check was narrower than the skill said.** `RequestInstants.cs:8,28,35` stops at depth 4 and `:51-57` walks only types in the request's root namespace, so an instant inside a `Dictionary<,>`, a tuple or deeper than four levels reaches Npgsql as a 500; `.claude/skills/api-design/SKILL.md:23` states the 400 unconditionally. e8eac0e's converter caught every body instant.
- **FIXED 2026-10-23: `SqlSelectBlocks.Clean` stripped comments before literals** (`SqlSelectBlocks.cs:42-43`): a literal containing `--` deletes the rest of the line before the string pass, so a scoped SELECT fails closed and an unscoped `FROM` later on the same line passes.
- **FIXED 2026-10-23: `-n` anywhere in a chained command counted as `--no-verify`** (`pre-commit.sh:32`): `grep -n foo file && git commit -m x` on a tree with code changes is refused with a message about a flag that was never typed. Fail-closed, so an improvement on the silent skip before 8b8c7c7, but misleading.
- **FIXED 2026-10-23: docs disagreed with the hooks.** `docs/DECISIONS.md:349` says `HookScriptTests` "runs every hook"; `docs/ARCHITECTURE_REVIEW.md:85` says the worktree hooks are untested, which `HookScriptTests.cs` confirms. `web/src/lib/api.ts:7-11,61-62` carries five- and two-line rationale comments against the one-line-trap rule.

Clean: 9bdc8ac, 75db6da, 37ebfff, e0c4046, b4e0668, 79c0507, e8eac0e, 9574d31, a47f800, f90832f, befe555, 638958c, 20819e6, c6e6856, and 8b8c7c7's migrator rewrite, `ci-status.sh` attribution and `pre-push` `GIT_*` unset.

Fixes, same day: `strip-quoted.py` strips both quote kinds in one left-to-right pass and prints the body of each `sh -c '...'` and `eval "..."` on a line of its own, so the guard reads a command hidden in one; `dotnet run|watch` and a bare `yarn` are heavy work; CI uploads `web/e2e-logs`; `doc-only` refuses `docs/exemplars/*` and any `.json`; the dev-cert trust step fails when it fails; `RequestInstants` walks generic containers, tuples and eight levels, keyed on the request's root namespace; `SqlSelectBlocks.Clean` strips literals and comments in one pass; `pre-commit.sh` reads `-n` only on the `git commit` segment; `JwtIdentityMiddleware` tags the activity with `tenant.id` beside the log push (the half of the derived projects' tenancy fix that is generic). Not fixed: the worktree hooks are still untested (`ARCHITECTURE_REVIEW.md` says so).

## Accepted with a trigger

- **Folder-only Clean Architecture (2026-06-09).** `Domain` is compiler-enforced; `Application`
  and `Infrastructure` are folders inside `StarterApp.Api`, so that boundary is
  convention-enforced. A full assembly split was designed and deferred as a large refactor for
  marginal gain at three aggregates. The modular-monolith target it would grow into is recorded in
  `DECISIONS.md`. Revisit if the domain grows or a compiler-enforced guarantee is required.
- **MFA truth is delegated to the IdP (2026-08-01).** `SecuredBy2Fa()` checks that `amr` contains
  `mfa`; whether that reflects real MFA is IdP policy outside this repo. Deployers enforce MFA in
  the IdP; the API check is a backstop.
- **Audit and archive blobs are not WORM-protected (2026-06-10).** The repo cannot express an
  immutability policy (the emulator does not enforce one and there is no IaC by decision).
  Deployers apply a time-based immutability policy aligned with `PayloadCapture:RetentionDays`.
- **Product create is not retry-idempotent under commit ambiguity (2026-06-10).**
  `CreateProductCommandHandler` has no natural key and a database-generated int id, so a
  commit-succeeded-but-ack-lost retry can insert a duplicate. The fix (client v7 id or a unique
  business key) is a stakeholder decision left open. Customer create is idempotent via its
  owner-scoped unique email.
- **Product read and write contracts name price differently (2026-09-03).** `price`/`currency`
  on create, `priceAmount`/`priceCurrency` on read. A rename was reverted because the by-id read is
  cached for ten minutes without a schema token, so cached products deserialized with `Price = 0`
  for the TTL. Trigger: a deliberate contract version (`Product:v2` cache key plus an API version),
  never a rename alone.
- **Dev and E2E depend on the Keycloak container (2026-08-01).** Only the Aspire-collection facts
  carry the dependency; unit and integration tests use a self-issued RSA test signer with an
  in-memory JWKS. Watch for drift between the committed realm file and what k6, DAST, and the
  smoke test expect.
- **Entity references are inferred from payload property names (2026-06-12).** `*Id` suffix plus
  sensitive-name screening, not per-endpoint declarations, because capture runs before routing and
  must cover rejected traffic. The September review fixed malformed-JSON masking and sensitive-ancestor traversal
  without changing that decision.

## Watch-items

- **Functions host logs rethrown invocation failures unredacted.** `MessageSettlement` no longer
  logs exception objects, but the host runtime can log propagated settlement or cancellation
  failures itself, outside the worker's redaction. Handler failures are now consumed by explicit
  bounded retries and PII-free settlement logging. Harmless until subscribers deserialize payloads. Close when real event
  parsing lands: filter host invocation-failure logging or add a redaction processor to the
  worker's OTel pipeline.
- **Subscriber slots hold for the whole retry deadline during an outage.** With
  `maxConcurrentCalls: 16`, a sustained FailClosed archive outage stalls the subscription for its
  duration and stretches time-to-dead-letter to roughly five deliveries times the deadline plus
  lock lapse. That is the intended backpressure; `DECISIONS.md` states it beside the timing.
  Revisit if an outage post-mortem shows the stall, not the outage, was the incident.
- **`aspire` CI flake on Service Bus emulator readiness.** Cold-runner emulator start-up can time
  out the healthy-API fact. The Aspire facts are `[AspireFact]`s, run only in the `aspire` job
  with `STARTERAPP_ASPIRE_TESTS=true` and skipped everywhere else, so a flake there never touches
  the unit or integration jobs. The shared E2E fixture gates every fact on API readiness (5-minute
  budget); subscriber-dependent facts opt in via `EnsureFunctionsReadyAsync()` (10-minute budget),
  and since 2026-09-06 the outbox end-to-end fact awaits that gate itself, so a recurrence is a
  genuine boot timeout. Raise the timeout if it recurs.
- **`IArtifactCaptureSink` has no producer.** The slot shipped ahead of any producer on
  2026-06-12. Wire the first artifact producer through it; if a year passes with none, reopen the
  keep decision.

## Deferred with named triggers

- **Doc-mirror generator.** Since 2026-09-25 `AGENTS.md` holds the instructions and `CLAUDE.md`
  is a one-line `@AGENTS.md` import; there is no mirror. Trigger: a harness that reads neither
  `AGENTS.md` nor an import and needs its own copy.
- **Per-stage capture-sink failure isolation.** Trigger: a deployment opts the HTTP channel into
  FailClosed, or duplicate archive rows become a support problem. Under ServiceBus FailClosed an
  entity-index failure after the archive append rethrows, and the subscriber's in-process retry
  appends the same archive line again (up to six per delivery, never a loss); the
  [2026-09-06 record](reviews/ARCHITECTURE_REVIEW-2026-09-06.md) has the analysis.
- **Module-scoped agent docs.** A single root agent doc works at the current size. Trigger: the
  template grows into multiple modules. Then the root keeps vision, build and test commands, and an
  index; each module gets its own doc with business rules, command and event inventory, and a
  pre-change checklist; `AGENTS.md` stays a pointer to the root doc.
- **Compiler-enforced module boundaries.** Trigger and design are in the folder-only entry above
  and the modular-monolith decision in `DECISIONS.md`.
- **Broker-observed settlement tests.** Retry-then-complete, abandon-then-redeliver, dead-letter
  reason, and lock renewal across the 210 s deadline are proven only against a fake
  `ServiceBusMessageActions` plus constant arithmetic; no test opens a receiver or reads a
  dead-letter queue. Assessed 2026-09-06: `Testcontainers.ServiceBus` wraps the same emulator
  image the Aspire fixture already runs (`App.GetConnectionStringAsync("servicebus")` hands a
  test a live broker today), and the Spotflow in-memory package fakes the SDK, not the Functions
  host where the risk lives, so neither is adopted. Trigger: the first subscriber that
  deserializes an event (a poison path exists), or a `MessageLockLost` seen in the `aspire` job
  or production logs. Then reference `Azure.Messaging.ServiceBus` from `StarterApp.AppHost.Tests`,
  add one fact that publishes a malformed body and reads `$deadletterqueue` for reason
  `JsonException`, and give the `aspire` job a `timeout-minutes`. Details in the
  [2026-09-06 record](reviews/ARCHITECTURE_REVIEW-2026-09-06.md#continuation--emulator-assessment).

## Process

Read this file before any review or hardening task. Each review gets a dated record under
`docs/reviews/`; this file links it and lists its open findings in one line each. When a finding
is fixed: land the regression test in the same change, write the resolution in the dated record,
and remove the finding from this file. Adjust the score conservatively, and only on verified
fixes. Dismissed false positives go in the record so they are not re-raised. This file and
`docs/investigations/` are the sync points across concurrent agent sessions.
