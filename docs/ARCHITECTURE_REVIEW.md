# Architecture Review

Living state only. Narrative review history (every dated session note, resolved-finding table,
and dismissed-false-positive record through 2026-06-12) is preserved verbatim in
[docs/reviews/ARCHITECTURE_REVIEW-2026-06-archive.md](reviews/ARCHITECTURE_REVIEW-2026-06-archive.md).
The post-IdP conversion review and its four open findings are recorded in
[docs/reviews/ARCHITECTURE_REVIEW-2026-08-04-post-idp.md](reviews/ARCHITECTURE_REVIEW-2026-08-04-post-idp.md).
The same-day runtime-hardening review and its five open findings are recorded in
[docs/reviews/ARCHITECTURE_REVIEW-2026-08-04-runtime-hardening.md](reviews/ARCHITECTURE_REVIEW-2026-08-04-runtime-hardening.md).
The 2026-09-02 whole-solution review (five Medium, seventeen Low, three dismissed candidates) is
recorded in
[docs/reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md)
(a rendered, shareable version of the same findings sits beside it as
[ARCHITECTURE_REVIEW-2026-09-02-whole-solution.html](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.html)).
Skeptics verifying "was this already dismissed?" consult the archive; this file answers "what is
true and open right now".

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices
across a deliberately small e-commerce sample domain (Products, Customers, Orders) with Aspire
orchestration and PostgreSQL. The template is agent-maintained; its heavy machinery is a
deliberate design stance (patterns are the pedagogy, convention tests are the product), not
accidental weight — a 2026-06-12 juice-vs-squeeze complexity review confirmed the stance and
pruned what failed it (see `docs/ROADMAP.md`, complexity-review backlog).

**Score: 8.0/10** — recovered 2026-09-03 when the nine 2026-08-04 findings (one high, eight
medium) landed with regression tests (twenty-six of the thirty-one closures that day carry a
dedicated regression test; five are verified manually and say so), recovering the 0.9 dip those
reviews recorded (was 7.1 after
the twenty-two 2026-09-02 whole-solution findings were resolved the same day; 6.9 after that review
found them; 7.1 after the 2026-08-04 runtime-hardening review; 7.7 after the same-day post-IdP
review; 8.0 after the gateway→OIDC identity conversion, independently re-scored 2026-06-09 on a
strict production scale). The conversion trades a distinctive strength (individually-signed
gateway assertions) for zero-trust posture (the API verifies the caller's own credential,
asymmetric JWKS, no shared secrets); the remaining dip is the open sender-constraining finding.
Revisit the identity posture when DPoP/mTLS-binding lands or the replay finding is re-accepted
with evidence.
**Read this before trusting the number**: the score is self-assessed by the maintaining agents
(Claude and Codex across sessions) with no external human validator and no fixed rubric; treat it
as a maintenance log, not an audit. The historical self-graded 9.7 was stale/monotonic — the
archive retains it for provenance only. Held below 9 by the open findings, folder-only Clean
Architecture deferral, and accepted limitations below.

Verifiable snapshot (re-verify, don't trust): 9 command handlers, 7 query handlers, every
command/query validated (convention-enforced), 0 CQRS violations, full suite ~740 tests green
plus AppHost integration tests; the nightly k6 perf gate and DAST scan both pass on `main`.

## Strengths (compressed — the archive carries the full analysis)

Convention-enforced boundaries (110+ mechanical rules incl. supply-chain, doc-mirror, event
coverage); rich DDD aggregates with client-generated v7 ids for creation-event aggregates;
strict CQRS (EF commands / Dapper reads); transactional outbox with claim/salvage, per-cause
retry budgets, replay verb + runbook, and pinned event-contract snapshots; full payload
capture/audit posture with per-channel failure policy, owner-scoped redaction rules, and
correlation-bound artifact slot; zero-trust OIDC/JWT identity validated in the API itself
(asymmetric JWKS, no shared secrets, no bypass mode); owner-scoping enforced in predicates, policy, cache keys, and rate-limit
partitions, with policy invocation structurally verified in the mediator pipeline; refresh-ahead
caching with serve-stale-on-error; job-run history; incident knowledge base + reporting pack,
both schema-guarded; supply-chain hardening (CPM + locked-mode, digest-pinned images, SHA-pinned
actions, gitleaks, Dependabot, CodeQL); k6 perf gate (nightly, seeded, volume-floored) and ZAP
DAST, both green.

## Open Findings

Detailed evidence and verification for the four 2026-08-04 post-IdP findings and the five
runtime-hardening findings live in their dated records; all nine were resolved on 2026-09-03 (see
the RESOLVED entries below and each record's Resolution section).
Detailed evidence, the seventeen Low findings, and the three dismissed candidates from the
2026-09-02 pass live in the
[whole-solution review](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md); all
twenty-two were resolved on 2026-09-03 (its Resolution section, and the RESOLVED entries below).



Decisions / watch-items / explained deferrals follow.

- **RESOLVED (2026-09-03) — quoted database passwords could leak into logs.** The three regex
  masks are gone. `ConnectionStringDescriptor.Describe` (ServiceDefaults, linked into the migrator
  as a source file) parses with `NpgsqlConnectionStringBuilder` and emits only host, port,
  database and user; a parse failure returns a fixed placeholder, never the input. Regression:
  `ConnectionStringDescriptorTests` (quoted semicolons, `Pwd` alias, malformed input).
- **RESOLVED (2026-09-03) — errored outbox rows could lose their replay-retention window.**
  `OutboxMessage.ErroredOnUtc` (migration `0006`, back-filled to `now()` for existing errored rows)
  is stamped by `MarkAsError`, cleared by both replay paths, and retention for errored rows counts
  from it. Regression: `CleanupExpiredMessages_ShouldPurgeOnlyProcessedAndErroredRowsPastRetention`
  (an old event that just failed is kept), `OutboxMessageTests`, `OutboxReplayTests` (SQL and entity
  reset stay in step).
- **RESOLVED (2026-09-03) — payload cleanup had a fixed throughput ceiling.** `CleanupBatchSize` is
  now a page size; `PayloadArchiveCleanupDrain` drains pages per prefix until caught up or
  `CleanupTimeBudgetSeconds` (default 1200) is spent, and a run that hits the budget reports
  `BudgetExhausted`, logs a warning, and records `Degraded` in `job_runs`. Regression:
  `PayloadArchiveCleanupDrainTests`, `InMemoryPayloadArchiveStoreTests` (drains beyond one page).
- **RESOLVED (2026-09-03) — concurrent customer creation could return another request's row.**
  The natural-key recovery lookup runs only on a genuine retry and accepts the row only when its
  name matches the request; a first-attempt race goes through the unique constraint to a 409.
  Regression: `Handle_WhenConcurrentCreatesRaceOnTheSameEmail_OnlyOneSucceedsAndEachResultIsItsOwn`
  (six parallel creates against PostgreSQL; the assertion that every returned DTO is the caller's
  own is the property the old code could violate, so the test is deterministic for the fix and
  timing-dependent as a detector of the old bug).
- **RESOLVED (2026-09-03) — Redis failures took healthy reads down.** `CachingBehavior` treats a
  failed cache read as a miss, skips repopulation when the tombstone cannot be checked, and
  swallows a failed write, logging each; cancellation still propagates. Regression: four
  `CachingBehaviorTests` facts (read, tombstone, write, cancellation).
- **RESOLVED (2026-09-03) — CORS did not expose bearer challenges.** Both policy branches expose
  `WWW-Authenticate`, `X-Correlation-ID` and `Retry-After`. Regression: `CorsPolicyTests` over the
  development and production branches.
- **RESOLVED (2026-09-03) — malformed OIDC authorities passed startup validation.**
  `AuthorityIsWellFormed` requires an absolute http(s) URI and https unless `RequireHttpsMetadata`
  is false, in every environment. Regression: `JwtIdentityOptionsTests` (relative, garbage, ftp,
  http-with-https-required, http-allowed-in-development, https).
- **RESOLVED (2026-09-03) — the identity convention missed raw Authorization-header reads.**
  `ClaimsPrincipal_MustOnlyBeReadByIdentityInfrastructure` now also fails on
  `IHeaderDictionary.get_Authorization`, the `HeaderNames.Authorization` field, and the literal
  outside the identity namespace (the composition root's CORS allow-list is the one exemption).
  Proven against an injected endpoint-side read, then reverted.
- **RESOLVED (2026-09-03) — OIDC tooling had an undeclared Python dependency.** The smoke test
  defines its python3-or-grep JSON helper before the first parse and uses it for the authority and
  token responses; `dev-idp.sh` parses tokens with jq, then a proven-runnable python3, then sed, so
  DAST and the perf gate need neither. Verified with python3 and jq removed from `PATH`.

- **RESOLVED (2026-09-03) — exception-mapped responses lost every security header and the
  correlation-id echo.** `UseSecurityHeaders` and `PayloadCaptureMiddleware` now register
  `Response.OnStarting` callbacks, which survive the `Response.Clear()` inside
  `UseExceptionHandler`. `UseHsts()` stays as the framework middleware (validation pass: a
  hand-rolled copy disabled the `AddHsts` extension point for no runtime gain — behind the TLS
  terminator `Request.IsHttps` is false either way). Regression:
  `ProblemDetailsTests.ErrorResponses_ShouldKeepSecurityHeadersAndCorrelationId` (404 and 400),
  proven to fail on the pre-fix pipeline.
- **RESOLVED (2026-09-03) — the payload-archive health check built a `BlobServiceClient` and
  `DefaultAzureCredential` per probe.** `AddPayloadCapture` registers one
  `PayloadArchiveClientProvider` per process, resolved from the bound options by
  `PayloadArchiveConfiguration` (the single owner of the resolution order); the archive store
  and the singleton check both take the client through it. Validation pass: a dedicated
  provider rather than a bare `BlobServiceClient` registration, so an unrelated Azure blob
  client in DI can never be picked up, and options (not raw config) decide at resolution time.
  Regression: `PayloadArchiveHealthCheckRegistrationTests`.
- **RESOLVED (2026-09-03) — route-path entity references bypassed the sensitive-name filter.**
  `sensitiveTokens` is threaded into `AddRouteReference` and applied to caller-supplied
  `EntityReferences`, so every source passes the same screen. Regression:
  `PayloadCaptureTests.Extract_WithSensitiveRoutePathOrCallerSuppliedReferences_ShouldNotEmitThem`.
- **RESOLVED (2026-09-03) — four raw IL byte loops in convention tests.** All four sites walk via
  `IlInstructionWalker` (linked into `StarterApp.AppHost.Tests`, which has no reference to
  `StarterApp.Tests`). Meta convention
  `HousekeepingConventionTests.TestSourcesThatReadIl_MustWalkOnInstructionBoundaries` fails any
  test source that reads IL bytes without walking; proven against the pre-fix
  `DomainConventionTests`.
- **RESOLVED (2026-09-03) — `scripts/reset-servicebus-emulator.sh` failed on bash 3.2 and skipped
  the network step.** Rewritten without bash 4 builtins, removes the pair's Aspire networks after
  the containers, linked from the development-workflow skill's recovery section. Verified by
  running under macOS `/bin/bash` 3.2.57.
- **RESOLVED (2026-09-03) — sixteen of the seventeen Low findings from the same review**
  (validator/domain drift on order totals and product currency, job-run purge orphaning,
  duplicate correlation ids, `Environment.Exit` skipping the log flush, migrator references,
  redundant indexes, stalled-outbox visibility, probe capture, rate-limit queueing and
  `Retry-After`, read-retry jitter and budget, worktree-scoped housekeeping, dead code, doc
  precision). Each fix and its test are listed in the whole-solution review's Resolution section,
  together with the same-day validation pass that trimmed four of them (see below).
- **ACCEPTED (2026-09-03) — Product read and write contracts name price differently
  (`price`/`currency` on create, `priceAmount`/`priceCurrency` on read).** Fixed, then reverted on
  validation: `GetProductByIdQuery` is cached for 10 minutes without a schema token in the key, so
  renaming the read model made every cached product deserialize with `Price = 0` for the TTL and
  the whole rolling-deploy overlap — a live defect traded for a cosmetic Low. Re-add trigger: a
  deliberate contract version (`Product:v2` cache key plus an API version), never a rename alone.
- **Validation pass (2026-09-03).** Three independent reviewers re-read both fix commits with
  instructions to prove each change unnecessary or oversized. Most held; these were trimmed in
  the same day: the hand-rolled HSTS header (framework `UseHsts` restored), the rate-limit
  `QueueLimit` change (the class default moved to 0 but `appsettings.json` still shipped 5; on
  re-test, 0 rejected bursts the integration suite legitimately produces, so the queue of 5 is
  kept as deliberate smoothing on both sides, `Retry-After` stays, and a test pins the file to
  the class defaults), the payload-cleanup page loop (re-listing the
  non-chronological `entity-index/` prefix per page was quadratic — now one pass per prefix
  with an inline, per-prefix time budget and no page cap), the invented 10,000 order-quantity
  ceiling (removed; the guard now checks the prospective GST-inclusive order total, which is
  what the outbox capture actually computes), the paused-batch rule (Degraded only when a
  window paused and published nothing), the composition-root exemption in the identity
  convention (now literal-only), the single-shot job-run path's purge isolation, and the
  emulator reset script's network removal (skips a network any container, running or stopped,
  is still attached to). Closures without a dedicated regression test — findings 5, 11, 12, 13
  and the customer race (timing-dependent as a detector) — are verified manually and named as
  such; the score does not claim otherwise.

- **RESOLVED (2026-07-18) — Aspire-collection trait pairing was not mechanically enforced.**
  The CI unit job excludes Aspire E2E facts with `Category!=Aspire`; that filter is only sound if
  every `[Collection("Aspire E2E")]` class also carries `[Trait("Category","Aspire")]`. Both
  current members did, but nothing prevented a future Aspire test joining the collection without
  the trait (and without "Integration" in its name) from booting the full distributed rig inside
  the unit job, where nothing is provisioned for it. Fixed by
  `AspireCollectionTraitConventionTests.EveryAspireCollectionMember_MustCarryTheAspireCategoryTrait`,
  which reflects over the AppHost.Tests assembly and fails the build on any collection member
  missing the trait.
- **RESOLVED (2026-07-18) — Dead-letter description could echo payload-derived text.**
  `MessageSettlement` wrote `exception.Message` into the Service Bus dead-letter description —
  unredacted broker metadata no Serilog masking reaches. Harmless today (subscribers don't yet
  deserialize payloads; the only non-retryable types carry JSON paths, not values), but a latent
  PII leak once handlers parse domain events. Fixed pre-emptively: the description now carries only
  the exception type + correlation id (support jumps to the correlation-bound archive for the full
  payload); regression asserts the payload-derived message is absent. `MessageSettlementTests`.
- **RESOLVED (2026-07-18) — Functions retry window sat exactly on the lock-renewal ceiling.**
  `FunctionsHostConfigConventionTests` asserted `maximumInterval * maxRetryCount <=
  maxAutoLockRenewalDuration`, which passed only at the exact boundary (`60s * 5 = 300s = 300s`)
  and ignored per-attempt handler execution time. Fixed by requiring the worst-case window to stay
  within 80% of the lock window and dropping `maximumInterval` to 45s (worst case now 225s ≤ 240s),
  so a future nudge that erases the margin fails the build instead of shipping a zero-margin config.
- **RESOLVED (2026-07-18) — Field name interpolated into `python3 -c` in the smoke test.**
  `scripts/smoke-test.sh` `json_field()` built the Python source by interpolating `$field`; only
  script-literal constants were ever passed, but the field name is now passed via `sys.argv` so it
  can never be executed as code.
- **RESOLVED (2026-07-18) — Functions worker logged exception objects to an unredacted OTel sink.**
  Found by the post-commit security audit of the dead-letter fix above: `MessageSettlement` still
  attached the full exception object to its three failure-branch log calls, and the Functions
  worker has **no** redaction stage — its logs flow to OpenTelemetry via ServiceDefaults, while the
  `Serilog.Enrichers.Sensitive` masking stack lives only in the API. Same latent class as the
  dead-letter description: harmless until handlers deserialize payloads, then `exception.Message`
  leaks payload text into logs. Fixed: the log calls now emit exception type + correlation id as
  structured properties, never the exception object; regression
  (`SettleAsync_LogsNeverCarryTheExceptionObjectOrItsMessageText`) drives all three branches with a
  PII sentinel and asserts it reaches neither the rendered message nor the log event.
  **Residual channel (watch-item, same trigger):** the transient branch rethrows, and the Functions
  host runtime logs rethrown invocation failures itself, unredacted and outside settlement's
  control. Close when real event parsing lands: either filter host invocation-failure logging or
  add a redaction processor to the worker's OTel logging pipeline. Until then payload-echoing
  exceptions cannot occur (subscribers do not deserialize; `JsonException` carries paths, not
  values).

- **RESOLVED (2026-07-05) — Blanket BCL-exception → client-fault status mapping.**
  `ResolveExceptionStatusCode` mapped every `InvalidOperationException` to 409 and every
  `KeyNotFoundException` to 404, so a stray BCL throw from a genuine server bug (LINQ
  `.Single()`, a dictionary miss) surfaced as a client fault and hid from 5xx alerting. Fixed in
  the same change it was found: dedicated `DomainRuleException` (409) and
  `EntityNotFoundException` (404) in `StarterApp.Domain.Exceptions`, all intentional throw sites
  swept, bare BCL types now fall through to 500. Regression tests in
  `ExceptionStatusCodeMappingTests`; `ExceptionConventionTests` (IL `newobj` scan over Domain +
  Api Application types) blocks reintroduction.

- **Folder-only Clean Architecture — ACCEPTED (deferred; split designed, not implemented,
  2026-06-09).** `Domain` is compiler-enforced; `Application`/`Infrastructure` are folders inside
  `StarterApp.Api`, so that boundary is convention-enforced. A full assembly split was designed
  (Domain ← Application ← Infrastructure ← Api, with the EF `ApplicationDbContext` living in
  Application because the repository pattern is banned) and deliberately deferred: large
  high-churn refactor for marginal gain at 3 entities. Revisit if the domain grows or a
  compiler-enforced guarantee is required. Full design rationale in the archive.
- **RETIRED (2026-08-01) — the two gateway-assertion acceptances (replay window, unsigned
  body/query).** Both were conditioned on the trusted perimeter; the zero-trust requirement fired
  their shared revisit trigger and the assertion model itself was replaced with OIDC/JWT
  validated in the API (`docs/DECISIONS.md`, "OIDC/JWT identity"; old model at tag
  `pre-idp-conversion`). Superseded by the two entries below.
- **OPEN — bearer tokens are not sender-constrained (replay posture regressed vs the retired
  model).** The retired assertion was bound to method + exact path with a ~150s lifetime; an
  IdP-issued bearer token is valid for any endpoint in its audience until expiry. Mitigated by
  short access-token lifetimes and strict audience validation, but honestly a wider replay
  surface than before. Fix: DPoP (RFC 9449) or mTLS-bound tokens (RFC 8705) — Keycloak supports
  both locally; note Entra's narrower support constrains the production IdP choice. Trigger to
  implement: before any production deployment on an untrusted network, or the first time a token
  is observed outside its intended client.
- **MFA truth is delegated to the IdP — ACCEPTED (2026-08-01).** `SecuredBy2Fa()` enforces that
  the validated token's `amr` contains `mfa`, but whether that claim reflects real MFA is IdP
  realm/policy configuration outside this repo. Deployers: enforce MFA in the IdP; the API-side
  check is a backstop, not the source of truth.
- **Audit/archive blobs are not WORM-protected — ACCEPTED (2026-06-10, deployer guidance).**
  Anything with blob-delete rights can destroy audit records inside the retention window; the
  repo cannot express an immutability policy (emulator doesn't enforce it; no IaC by decision).
  Deployers: apply a time-based immutability policy aligned with `PayloadCapture:RetentionDays`.
- **Product create retry-idempotency under commit ambiguity — ACCEPTED (2026-06-10).**
  `CreateProductCommandHandler` has no natural key and a DB-generated int id, so a
  commit-succeeded-but-ack-lost retry can insert a duplicate row. The fix (client-generated v7 id
  or a unique business key) is a deliberate stakeholder decision left open; Customer create is
  idempotent via its owner-scoped unique email. Residual test gaps recorded in the archive.
- **Dev/E2E dependency on the Keycloak container — ACCEPTED (2026-08-01).** The Aspire run mode
  and AppHost E2E tests depend on the Keycloak container booting with the imported realm; unit
  and integration tests avoid it via the self-issued RSA test-JWT signer + in-memory JWKS, so
  only the Aspire-collection facts carry the dependency. Watch for realm-import drift between the
  committed realm file and what k6/DAST/smoke expect.
- **Watch-item — `aspire` CI flake (Service Bus emulator readiness).** Cold-runner emulator
  startup can time out `AppHost_ShouldEventuallyExposeHealthyApi`; raise the readiness timeout if
  it recurs. Mitigation underway: the shared E2E fixture (backlog item 13) cuts the boot count,
  and the fixture now gates every fact on the API readiness probe (5-minute budget) — which
  transitively proves the migrator and the emulator — instead of each fact polling for itself.
  The Functions container is deliberately outside that gate (its in-container image rebuild can
  dwarf every other boot cost); subscriber-dependent facts opt in via
  `EnsureFunctionsReadyAsync()` (10-minute budget), and `FunctionsContainerIntegrationTests`
  pins that the deployable subscriber image actually boots and serves.
- **ArtifactCaptureSink consume-or-reopen trigger (2026-06-12).** The artifact capture slot
  shipped deliberately ahead of any producer. Trigger: when the first artifact producer lands,
  wire it through `IArtifactCaptureSink`; if a year passes with no producer, reopen the
  keep-decision.
- **Entity-reference inference decision (2026-06-12, decision-on-file).** Payload entity
  references are inferred from payload property names (`*Id` + sensitive-name screening) rather
  than declared explicitly per endpoint — chosen because capture runs before routing
  (capture-first recorded decision) and explicit declarations cannot cover rejected/unmatched
  traffic, which is captured by design.

## Deferred with named triggers (from the 2026-06-12 complexity review)

- ~~Dead-letter-with-reason in the Functions subscribers~~ — DELIVERED ahead of the
  handler-logic trigger: `MessageSettlement` settles manually (host.json `autoCompleteMessages:
  false`) — non-retryable failures dead-letter with the exception type as the reason and a
  truncated description, transient failures ride the host retry policy in-process and abandon
  explicitly on the final attempt (prompt redelivery; the subscription's MaxDeliveryCount is the
  poison backstop). Regression tests: `MessageSettlementTests`.
- Doc-mirror generator — trigger: the mirror set grows beyond the root pair + skills.
- Per-stage capture-sink failure isolation — trigger: a deployment opts the HTTP channel into
  FailClosed.

## Process

Read this file before any review or hardening task; consult the archive before dismissing or
re-raising anything. After fixing a finding: add the regression test in the same change, mark it
here (move durable narrative to the archive on the next compaction), and adjust the score
conservatively — it dips on discovery and recovers only with verified fixes. Dismissed false
positives are recorded in the archive so they are not re-raised. This file is the sync point
across concurrent agent sessions, together with `docs/ROADMAP.md` and `docs/investigations/`.
