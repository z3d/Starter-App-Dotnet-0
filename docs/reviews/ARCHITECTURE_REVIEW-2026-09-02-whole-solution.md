# Architecture Review — Whole Solution

**Date:** 2026-09-02

**Snapshot:** `d9f60ed` (`origin/main`, three commits after the 2026-08-04 reviews)

**Focus:** full read of every source file, one finder per subsystem (identity/PII, domain and
application, persistence and eventing, convention-test rigor and CI, request pipeline and
concurrency), followed by independent adversarial verification of every Medium candidate.

## Assessment

The architecture holds. Every load-bearing control was re-derived from code rather than trusted:
JWT validation parameters, the single `ICurrentUser` writer, owner scoping in every SQL predicate
and every mutation handler, owner-hashed cache keys, one `SaveChangesAsync` per handler with the
order id minted outside the retry delegate, outbox claim/salvage under `SKIP LOCKED`, the
Service Bus topology bijection, and the exception-to-status table. None of them has regressed
since 2026-08-04, and the nine findings recorded then are all still open (the three commits since
touched product validation, a reset script, and a transitive pin).

What survived refutation this time is a different class from last time: not data-integrity or
credential bugs, but **cross-cutting middleware and tooling gaps** that no test exercises because
each one sits on an error path, a deployment configuration, or a platform the CI runner is not.
The two that matter operationally are the loss of every security header and the correlation-id
echo on exception-mapped responses, and a health check that builds a fresh Azure credential per
probe on two unthrottled, unauthenticated routes. Three candidates that looked like High-value
convention gaps were dismissed on verification because an existing analyzer, an existing test, or
a recorded decision already closes them; they are listed at the end so they are not re-raised.

Score moved from 7.1 to **6.9**. No High findings; five Medium, seventeen Low. The dip is small
because none of the new findings touches data integrity or identity, and recovers when the five
Mediums land with regression tests.

**Snapshot re-verified:** `dotnet build` clean; 551 of 606 tests in the "fast" filter pass on this
machine, the remaining 55 fail only because Docker was down (they share the Testcontainers
PostgreSQL fixture, as the CI workflow documents). 9 command handlers, 7 query handlers, all
validated, 0 CQRS violations.

## Findings

### 1. Exception-mapped responses lose every security header and the correlation-id echo

**Severity: Medium** | Files: `src/StarterApp.Api/Program.cs:61-66`,
`src/StarterApp.Api/Infrastructure/WebApplicationExtensions.cs:7-22`,
`src/StarterApp.Api/Infrastructure/Payloads/PayloadCaptureMiddleware.cs:57`

Both header writers are eager: payload capture sets `X-Correlation-ID` before calling `next`, and
`UseSecurityHeaders` appends `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and
CSP before calling `next`. `UseExceptionHandler` sits between them and calls `Response.Clear()`
before writing ProblemDetails, which wipes every header set so far and never re-runs the
downstream middleware. `UseHsts` is eager too, so `Strict-Transport-Security` is lost as well.
Every validation 400, 404, 409, 503 and 500 therefore ships with no security headers and no
correlation id, which breaks the documented "jump from the response's correlation id to the
archive blob" support workflow on exactly the responses support is asked about. CORS headers
survive only because `CorsMiddleware` uses `OnStarting`. No integration test asserts a header on
an error response; `ProblemDetailsTests` checks body fields only and
`PayloadCaptureIntegrationTests` calls `EnsureSuccessStatusCode` before asserting the echo.

**Fix:** Register both header sets via `context.Response.OnStarting(...)` (reordering the
middleware does not help; `Headers.Clear()` still runs). Add an integration test that triggers a
409 and asserts `X-Correlation-ID` plus the security headers are present.

### 2. The payload-archive health check builds a new `BlobServiceClient` and `DefaultAzureCredential` per probe

**Severity: Medium** | Files:
`src/StarterApp.Api/Infrastructure/HealthChecks/PayloadArchiveHealthCheck.cs:17-26`,
`src/StarterApp.Api/Infrastructure/ServiceCollectionExtensions.cs:217`, `src/StarterApp.Api/Program.cs:87`

The check constructs its client in the constructor and is registered only through
`AddCheck<T>`, never in DI, so `HealthCheckService` activates a fresh instance on every run. In
the managed-identity configuration (`AccountUri`, no connection string) each probe creates a
`DefaultAzureCredential` with an empty per-instance token cache and performs a full credential
chain walk plus an IMDS token fetch before `GetPropertiesAsync`. The check is reachable from two
unauthenticated, rate-limit-exempt routes: `/healthiness` (durable tag) and the unpredicated
`/health`, which runs every registered check and is hammered by the k6 smoke. IMDS throttling
would flip both probes to 503 on a healthy storage account. The three sibling checks inject their
clients; a warm singleton client already exists in `AddPayloadCapture`.

**Fix:** Expose the singleton `BlobServiceClient` (or `IPayloadArchiveStore`) in DI and inject
it, collapsing the duplicated connection-string resolution. Consider a predicate on `/health` so
the root probe stays cheap.

### 3. Route-path entity references bypass the sensitive-name filter

**Severity: Medium** | Files:
`src/StarterApp.ServiceDefaults/Payloads/PayloadEntityReferenceExtractor.cs:57,178-194,274-286`

The extractor's own header comment and `docs/DECISIONS.md` state that a sensitive `*Id` such as
`nationalId` never becomes a blob path segment. The metadata and JSON-body paths honour that by
passing `sensitiveTokens` through `IsSensitivePropertyName`; the route-path branch does not
receive the token list and `AddReference` does no screening. Capture runs before routing and
authentication and records 404 traffic by design, so an unauthenticated
`GET /api/v1/nationalId/123-45-6789` yields an entity reference that lands in the
`entity-index/nationalid/123-45-6789/...` blob name and in an Information-level log line that the
Serilog masking stack (email, IBAN, card shapes only) does not touch. The same datum in a JSON body
is suppressed, and the existing test covers only that door. Today no template route carries PII
in the path, so the leaked value is caller-supplied; the exposure is a broken documented
invariant that a derived project would inherit silently. The caller-supplied
`request.EntityReferences` list at lines 54-55 is unscreened for the same reason.

**Fix:** Thread `sensitiveTokens` into `AddRouteReference` and skip any pair whose entity segment
matches; screen `request.EntityReferences` the same way. Extend
`Extract_WithSensitivePropertyNames_ShouldNotEmitThemAsEntityReferences` with a route-derived
case.

### 4. Four hand-rolled raw IL byte loops remain, two of them behind negative assertions

**Severity: Medium** | Files: `src/StarterApp.Tests/Conventions/DomainConventionTests.cs:303`,
`src/StarterApp.Tests/Conventions/CachingConventionTests.cs:216`,
`src/StarterApp.AppHost.Tests/PayloadCaptureConventionTests.cs:51`,
`src/StarterApp.AppHost.Tests/ProductionAssemblyConventionTests.cs:81`

`testing-strategy/SKILL.md:18` says never to hand-roll a raw IL byte loop, and
`IlInstructionWalker` exists for that reason. These four scan `il[i] is 0x28 or 0x6F` byte by
byte and only advance past operands after a call-looking byte, so any other opcode's operand
bytes are read as opcodes and a false hit skips the next four bytes. The verifier corrected the
finder's mechanism: garbage tokens throw inside a `try/catch` and are swallowed, so the hazard
is not a false positive but a **skipped genuine call**. That is a silent pass for the two
negative checks (`RaiseDomainEvent` must not be called from a constructor; production code must
not call `DateTime.UtcNow`) and for `ResolveCalledMembers`, which is the cohort-discovery filter
for `CommandHandlers_MutatingCacheableEntities_MustInvokeCacheInvalidator`, where a miss quietly
drops a handler from the rule's scope. No meta convention forbids the pattern.

**Fix:** Rewrite the two `StarterApp.Tests` sites over `IlInstructionWalker.Walk` (template:
`ExceptionConventionTests.ConstructsType`). Link `IlInstructionWalker.cs` into
`StarterApp.AppHost.Tests` via `<Compile Include=... Link=...>` and rewrite the other two. Add a
housekeeping convention that fails on `0x28`/`0x6F` literals outside the walker.

### 5. The Service Bus emulator reset script cannot run on macOS and performs half the documented recovery

**Severity: Medium** | Files: `scripts/reset-servicebus-emulator.sh:18,27`,
`.claude/skills/development-workflow/reference/service-bus-emulator.md:85-90`

`mapfile` is a bash 4 builtin; `/bin/bash` on the documented Apple Silicon dev target is 3.2.57
and `command -v bash` resolves to it, so the script exits 127 under `set -e` before touching a
container. Reproduced locally. It also removes only the two containers, while the skill says the
stale Aspire network is "the most common reason a clean retry fails the same way", and it then
prints that emulator state is cleared. Nothing references the script: not CLAUDE.md, not the
skill, which still documents the manual procedure. It is the only bash-4 construct in the repo and
there is no shell lint in CI.

**Fix:** Replace `mapfile` with a `while read` loop over an initialised array, add the
`docker network rm` step, link the script from the skill's recovery section, and consider
filtering on Aspire's DCP label rather than a `servicebus-*` name prefix.

### 6. Migration-safety conventions scan only the top level of `Scripts/`

**Severity: Low** | Files: `src/StarterApp.Tests/Conventions/PersistenceConventionTests.cs:264,277,322,417`

The numbered-prefix, contiguity and named-constraint tests use `TopDirectoryOnly`; the embedding
test at line 301 is recursive, and so are the csproj glob and DbUp's unfiltered
`WithScriptsEmbeddedInAssembly`. A script in a subdirectory would run, with an unnamed constraint
or a colliding number, unseen. Worse, DbUp orders by full resource name, so every nested script
runs after every top-level one regardless of number. Latent: no subdirectory exists today.

**Fix:** `SearchOption.AllDirectories` at all four sites, or a test asserting `Scripts/` is flat.

### 7. `Money.Add` bypasses `Money.Create`

**Severity: Low** | Files: `src/StarterApp.Domain/ValueObjects/Money.cs:63`

`Add` calls the private constructor while `Subtract` routes through `Create`, so `Add` is the one
construction path that can exceed `MaxAmount` and lacks the `ThrowIfNull`. No production caller
today; `OrderMapper.cs:28` wrongly credits `Add`/`Subtract` with currency enforcement.

**Fix:** `return Create(Amount + other.Amount, Currency);` plus `ThrowIfNull`; fix the comment.

### 8. Order line totals have no validator bound, so `MaxAmount` surfaces as a BCL exception from outbox capture

**Severity: Low** | Files: `src/StarterApp.Api/Application/Validators/CreateOrderCommandValidator.cs:32`,
`src/StarterApp.Domain/Entities/OrderItem.cs:55`

Quantity has no ceiling and nothing bounds `unitPrice × quantity`. A valid product at the price
ceiling with a large quantity passes validation and throws `ArgumentOutOfRangeException` from
`OrderCreatedDomainEvent` inside `SavingChangesAsync`. The handler's transaction rolls back the
stock reservation and the client gets a 400, but with a BCL message rather than a structured
validation error naming the line. Both product validators mirror `MaxAmount`; the order path
does not.

**Fix:** Bound quantity in the validator and add a `DomainRuleException` guard in `Order.AddItem`
when the line total would exceed `Money.MaxAmount`.

### 9. Create and update disagree on whether `Currency` may be absent

**Severity: Low** | Files: `src/StarterApp.Api/Application/Commands/CreateProductCommand.cs:11`,
`src/StarterApp.Api/Application/Commands/UpdateProductCommand.cs:9`

Commit `3573e99` made `Price` and `Stock` nullable on create "matching `UpdateProductCommand`",
but left `Currency` initialised to `"USD"`, while update declares it `string?` and rejects
absence. The same body creates a USD product on POST and fails with "Currency is required" on
PUT. Either is defensible; the two endpoints should agree.

**Fix:** Make create's `Currency` nullable and required, or document the default and relax update.

### 10. The Product read and write contracts name the same values differently

**Severity: Low** | Files: `src/StarterApp.Api/Application/DTOs/ProductDto.cs:8`,
`src/StarterApp.Api/Application/ReadModels/ProductReadModel.cs:8`

POST returns `price`/`currency`; GET returns `priceAmount`/`priceCurrency`, leaking the column
aliases into the contract. Customer and Order pairs agree.

**Fix:** Alias the read SQL and rename the read model to `Price`/`Currency`.

### 11. A failed job-run retention purge orphans a successfully inserted run

**Severity: Low** | Files: `src/StarterApp.ServiceDefaults/Jobs/JobRunRecording.cs:38-60,110-124`

`PurgeIfDueAsync` shares the insert's `try`, is due on the first run of every process, and
`_lastPurgeUtc` is stamped before the delete. If the bulk delete fails after the insert commits,
`StartRunAsync` returns `Guid.Empty`, `CompleteRunAsync` skips, and the row stays
`completed_on_utc IS NULL` forever, which the reporting pack reports as a crash.

**Fix:** Give the purge its own `try`, return `runId` regardless, and stamp `_lastPurgeUtc` only
after success.

### 12. Service Bus subscribers resolve the correlation id twice

**Severity: Low** | Files: `src/StarterApp.Functions/InventoryReservationFunction.cs:27,36`,
`src/StarterApp.Functions/OrderConfirmationEmailFunction.cs:27,36`

`RunAsync` resolves and pushes an id, then `ProcessAsync` resolves again. The final fallback is
`CorrelationContext.Create()`, a fresh value each call, so a message with neither source (a
copy-and-send resubmit per the replay runbook) is logged under one id and archived under another.

**Fix:** Read `CorrelationContext.GetOrCreate()` in `ProcessAsync`, or pass the resolved id in.

### 13. `Environment.Exit` skips `Log.CloseAndFlush()` in both hosts

**Severity: Low** | Files: `src/StarterApp.DbMigrator/Program.cs:45-78`, `src/StarterApp.Api/Program.cs:104-112`

Every exit path in the migrator, and the API's fatal-startup path, call `Environment.Exit`, which
does not unwind, so the `finally` flush is dead code. Seq and OpenTelemetry sinks batch, so the
one line explaining a failed migration, a replay, or a fatal startup can be lost.

**Fix:** Return an exit code from top-level statements (migrator) and set `Environment.ExitCode`
(API) so `finally` runs; or flush immediately before each exit.

### 14. DbMigrator carries an unused ServiceDefaults reference and an unused file sink

**Severity: Low** | Files: `src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj:21,31`

No migrator source references any ServiceDefaults type or `WriteTo.File`. The reference pulls
Azure, OpenTelemetry, service discovery, resilience and the ASP.NET framework into a 68-package
closure for a job that needs DbUp, Npgsql, Serilog and configuration.

**Fix:** Drop both, `dotnet restore --force-evaluate`, commit the regenerated lock.

### 15. Three redundant indexes on write-path tables

**Severity: Low** | Files: `src/StarterApp.DbMigrator/Scripts/0001_CreatePostgresSchema.sql:32,56,105`

`ix_outbox_messages_unprocessed` is a strict subset of `ix_outbox_messages_claimable` (same
partial predicate, leading column pinned to NULL by that predicate); the two-column
`ix_orders_tenant_id_owner_subject` and `ix_customers_tenant_id_owner_subject` are prefixes of
adjacent wider indexes. Every outbox, order and customer insert maintains an extra entry.

**Fix:** `0005_DropRedundantIndexes.sql` dropping the three by name.

### 16. A fully stalled outbox is indistinguishable from an idle one in `job_runs`

**Severity: Low** | Files: `src/StarterApp.Api/Infrastructure/Outbox/OutboxProcessor.cs:165,193`,
`src/StarterApp.Api/Infrastructure/Outbox/OutboxRunAggregator.cs:21-35`

Both pause-the-batch branches `break` without touching the aggregator, which emits nothing for an
all-zero window. With the archive down under FailClosed, hours of zero throughput leave no
`Degraded` row. Health checks and the pending-count SQL do surface the cause, hence Low.

**Fix:** Add a paused counter to the aggregator and report `Degraded` when it is non-zero.

### 17. `/liveness` and `/healthiness` are payload-captured on every poll

**Severity: Low** | Files: `src/StarterApp.Api/Infrastructure/Payloads/PayloadCaptureMiddleware.cs:31-37`,
`src/StarterApp.Api/Infrastructure/ProbeEndpoints.cs:19,25`

The exact-match skip set predates the two external-monitor routes, so each poll writes four blob
lines, compounding the open cleanup-throughput finding.

**Fix:** Add both to `ProbeSkipRoutes`, bump the count in the guarding test, update the
"four platform probe routes" sentence in `docs/DECISIONS.md`.

### 18. Rate-limit queueing stalls requests for up to a window and the 429 carries no `Retry-After`

**Severity: Low** | Files: `src/StarterApp.Api/Infrastructure/ServiceCollectionExtensions.cs:168-177`,
`src/StarterApp.Api/Infrastructure/RateLimitingOptions.cs:21`

`QueueLimit = 5` on a fixed window means requests 101 to 105 block until the next window rather
than failing fast; no `OnRejected` handler copies the limiter's retry-after metadata.

**Fix:** `QueueLimit = 0` and an `OnRejected` handler that sets `Retry-After`.

### 19. `PostgresRetryPolicy` has no jitter and retries connection exhaustion for about a minute

**Severity: Low** | Files: `src/StarterApp.Api/Infrastructure/Persistence/PostgresRetryPolicy.cs:15-17,86`

A deterministic 1-2-4-8-16-30 second ladder with `53300` in the transient set means saturated
reads retry in lockstep and hold requests open for 61 seconds, amplifying the saturation. The EF
write path uses the provider's jittered strategy.

**Fix:** Randomised jitter and an overall deadline on the read retry loop.

### 20. `ProjectFiles_MustNotReferenceBinOrObjArtifacts` walks gitignored sibling worktrees

**Severity: Low** | Files: `src/StarterApp.Tests/Conventions/HousekeepingConventionTests.cs:198-206,249-257`

Only `bin`, `obj` and `.git` are excluded, so `.claude/worktrees/**` (49 csproj files on this
checkout) is enumerated and another agent's uncommitted branch can fail this one's build. CI is
unaffected.

**Fix:** Scope to `src/` plus root props, or exclude `.claude`.

### 21. Dead code and hygiene

**Severity: Low** | Files: `src/StarterApp.Tests/Consistency/IlInstructionWalker.cs:64`,
`ASPIRE_SETUP_COMPLETE.md`, `.gitignore`

`IlInstructionWalker.GetOperandSize` has no callers. `ASPIRE_SETUP_COMPLETE.md` is tracked,
referenced by nothing, and omits Keycloak. `graphify-out/` sits untracked and unignored in a
shared checkout. Unreachable but harmless: `Customer.Activate`/`Deactivate`, `Order.RemoveItem`,
`Money.FromDecimal`, `ServiceDefaults.MapDefaultEndpoints`; the `AuditAction.Resolve` comment
describes a request `action` key that is never emitted; `src/StarterApp.Gateway/` is gitignored
`bin`/`obj` residue from before the OIDC conversion.

**Fix:** Delete the helper and the root doc, ignore `graphify-out/`, remove the Gateway residue,
fix the stale comment.

### 22. CLAUDE.md labels the unit filter "fast" without saying it needs Docker

**Severity: Low** | Files: `CLAUDE.md` (Commands), `AGENTS.md`, `.github/workflows/ci.yml:49-56`

55 handler tests inside the "fast" filter share a Testcontainers PostgreSQL fixture. CI documents
the pre-pull; the root doc does not, so a fresh agent on a Docker-less machine sees 55 failures.

**Fix:** One clause in both mirrors.

## Summary

| # | Finding | Severity |
|---|---|---|
| 1 | Error responses lose security headers, HSTS and `X-Correlation-ID` | Medium |
| 2 | Payload-archive health check builds a credential per probe on two unthrottled routes | Medium |
| 3 | Route-path entity references bypass the sensitive-name filter | Medium |
| 4 | Four raw IL byte loops, two behind negative assertions | Medium |
| 5 | Emulator reset script fails on bash 3.2 and skips the network step | Medium |
| 6-22 | Seventeen Low findings: validator/domain drift, contract naming, job-run trail, correlation ids, log flushing, redundant indexes and references, probe capture, retry and rate-limit shape, test scoping, hygiene | Low |

## Fix Order

1. Finding 1 (headers via `OnStarting`, one integration test) — smallest change, largest
   operational effect.
2. Finding 2 (inject the singleton blob client) — one registration line plus a predicate.
3. Finding 3 (thread `sensitiveTokens` into the route branch) — one parameter and one test case.
4. Finding 5 (portable script plus network removal plus skill link).
5. Finding 4 (walker migration and a meta convention).
6. Lows in any order; 6, 7, 11, 13 and 17 are each a few lines.

## Dismissed after verification — do not re-raise without new evidence

- **"Endpoints bind a `CancellationToken` but nothing checks it is forwarded."** `AnalysisMode=All`
  applies the SDK's `analysislevel_10_all.globalconfig`, which promotes CA2016 to warning, and
  `TreatWarningsAsErrors` makes it a build error. `.editorconfig` does not lower it. Every
  endpoint handler is a named method group, so the lambda blind spot does not apply. The
  convention test covers the declaring half; the analyzer covers the forwarding half.
- **"`GetExportedTypes` cohort discovery lets an `internal` handler escape the owner-policy and
  cache-invalidation conventions."** `EveryCommand_MustHaveAHandler` enumerates commands with
  `GetTypes()` and handlers with the exported-only helper, so an internal handler fails the build
  as a missing implementation. Coverage is transitive rather than intrinsic; switching the helper
  to `GetTypes()` is an optional one-line hardening, not a defect.
- **"Unauthenticated requests bypass the rate limiter because it runs after authorization."**
  Mechanically true and deliberate: `docs/DECISIONS.md` records that inbound volume is the
  upstream gateway's problem and scopes the in-app limiter to fairness between verified
  identities. Changing it is a new decision, not a bug. Residual precision nit: the documented
  IP fallback is live only for unmatched routes because every public mapped route disables
  limiting.

## Verification Performed

- Five finders each read every file in their area; each was told zero findings is acceptable and
  to attempt refutation before reporting. Eight Medium candidates went to independent verifiers
  instructed to refute; three were dismissed, two downgraded to Low, three confirmed at Medium
  with corrections to the finder's mechanism recorded above.
- Low findings were verified by the reviewing agent directly against the cited lines.
- Build and the fast test filter were run locally (551 pass; 55 Docker-dependent failures
  explained above). No code was changed.

## Resolution Standard

Per `docs/ARCHITECTURE_REVIEW.md`: each fix lands with a regression test proven to fail on the
injected regression, and the score recovers only on verified fixes.

## Resolution (2026-09-03)

All twenty-two findings were fixed in a single change on `main`, with the full test project
(718 tests, integration included) and the AppHost convention tests green. Regression tests for
the four code-level Mediums were proven to fail against the pre-fix files before the fix was
restored; the script was verified under macOS `/bin/bash` 3.2.57.

| # | Fix | Regression test |
|---|---|---|
| 1 | Security headers and the correlation echo applied via `Response.OnStarting` in `UseSecurityHeaders` and `PayloadCaptureMiddleware`; framework `UseHsts()` kept (validation pass); API start-up failure returns an exit code so `finally` flushes | `ProblemDetailsTests.ErrorResponses_ShouldKeepSecurityHeadersAndCorrelationId` |
| 2 | One `BlobServiceClient` per process registered by `AddPayloadCapture`, shared by the store and a singleton `PayloadArchiveHealthCheck`; `PayloadArchiveConfiguration` is the single resolution-order owner | `PayloadArchiveHealthCheckRegistrationTests` (two facts) |
| 3 | `sensitiveTokens` threaded into `AddRouteReference`; caller-supplied `EntityReferences` screened | `PayloadCaptureTests.Extract_WithSensitiveRoutePathOrCallerSuppliedReferences_ShouldNotEmitThem` |
| 4 | Four scans rewritten over `IlInstructionWalker.Walk`; walker linked into AppHost.Tests; `GetOperandSize` deleted | `HousekeepingConventionTests.TestSourcesThatReadIl_MustWalkOnInstructionBoundaries` |
| 5 | Script rewritten without bash 4 builtins; removes the pair's Aspire networks; linked from the skill | Manual: runs under bash 3.2.57 |
| 6 | Migration-safety guards enumerate `Scripts/` recursively | Existing guards now cover nested scripts |
| 7 | `Money.Add` routes through `Create`; both operators null-guard | `MoneyArithmeticTests` |
| 8 | `Order.AddItem` rejects an item that would take the prospective GST-inclusive order total past `Money.MaxAmount` with `DomainRuleException`, before mutating (validation pass replaced a per-line ex-GST check and dropped an invented quantity ceiling) | `OrderLineTotalTests` |
| 9 | `CreateProductCommand.Currency` nullable and required, matching update | `ValidatorBoundaryTests.CreateProductCommandValidator_WithAbsentCurrency_ShouldReturnValidationError` |
| 10 | Reverted on validation: the by-id product read is cached without a schema token, so the rename made cached products deserialize with `Price = 0` for the TTL. Recorded as ACCEPTED with a re-add trigger | — |
| 11 | Purge isolated in its own try on both recording paths; `_lastPurgeUtc` stamped only after success | None dedicated (needs fault injection into the delete); existing `JobRunRecorderTests` still green |
| 12 | `ProcessAsync` reads the pushed correlation id via `CorrelationContext.GetOrCreate()` | None dedicated |
| 13 | Migrator returns exit codes instead of `Environment.Exit`; API returns 1 on fatal start-up | None dedicated |
| 14 | ServiceDefaults reference and `Serilog.Sinks.File` dropped from DbMigrator; lock regenerated with `--force-evaluate`; Dockerfile COPY lines removed | Build and integration migrations |
| 15 | `0005_DropRedundantIndexes.sql`; matching `HasIndex` calls removed from EF configurations | Integration fixture runs the script |
| 16 | `OutboxRunAggregator.AddPaused()`, `Paused` in the window and summary JSON, `Degraded` when a window paused and published nothing (validation pass: routine throttling with publishes stays Succeeded); both pause branches count | `OutboxRunAggregatorTests.TryFlush_AfterInterval_WithOnlyPausedBatches_EmitsDegradedWindow` |
| 17 | `/liveness` and `/healthiness` added to `ProbeSkipRoutes`; DECISIONS.md updated | `ProbeSkipRoutes_CanNeverExcludeTheBusinessSurface` (count 6) |
| 18 | `OnRejected` sets `Retry-After` from limiter metadata. The queue of 5 is kept on validation (0 rejected legitimate bursts); a test pins `appsettings.json` to the class defaults so the two cannot drift | `RateLimitingTests` |
| 19 | Jittered backoff on a 0.5-to-5 second ceiling with a 10 second cumulative budget | `PostgresRetryPolicyTests` (jitter bounds, budget exhaustion) |
| 20 | `.claude` excluded from the housekeeping walk | Existing `ProjectFiles_MustNotReferenceBinOrObjArtifacts` |
| 21 | `GetOperandSize` and `ASPIRE_SETUP_COMPLETE.md` deleted; `graphify-out/` ignored | Build |
| 22 | CLAUDE.md and AGENTS.md note that the fast filter's handler tests need Docker | `AgentDocsConventionTests` mirror check |

Not changed: the unreachable domain methods listed under finding 21 (`Customer.Activate`,
`Order.RemoveItem`, `Money.FromDecimal`, `MapDefaultEndpoints`) were left in place as template
surface; remove them in a derived project.

## Validation (2026-09-03)

Three independent reviewers re-read the fix commits (`47d20e7`, `1e1df8b`) with instructions to
prove each change unnecessary or oversized. Verdicts: the Medium fixes and most Lows were needed
and proportionate. Trimmed the same day: hand-rolled HSTS (framework middleware restored),
`QueueLimit` (config file still shipped the old value), the payload-cleanup page loop (quadratic
on `entity-index/`; now one pass per prefix with an inline budget), the invented quantity ceiling
and the incomplete per-line guard (now the GST-inclusive order total), the paused-batch Degraded
rule, the composition-root exemption (literal-only), the job-run purge on the single-shot path,
the reset script's network removal guard, and the product read-model rename (reverted; cache-shape
hazard). Judged overkill but kept at the user's request: the stored HTML render of this review.
