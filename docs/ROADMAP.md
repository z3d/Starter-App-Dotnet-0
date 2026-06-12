# Improvement Roadmap

Last updated: 2026-06-10 (rev 4: added incident knowledge base and module-scoped agent docs.
rev 3: added event-contract shape snapshot guard and operator replay path. rev 2: dropped
spike-folder and production-IaC items per maintainer decision; moved the WORM audit-retention item
to `docs/ARCHITECTURE_REVIEW.md` as an accepted limitation with deployer guidance)

Forward-looking capability backlog for this template. It complements `docs/ARCHITECTURE_REVIEW.md`
(which tracks findings and fixes in the existing code) by tracking capabilities the template should
gain. Items follow the repo philosophy: mechanical rules over architectural taste, convention-test
enforcement wherever a rule can be made deterministic.

When an item lands: mark it done here with the commit/PR, add regression/convention tests with the
implementation, and update `CLAUDE.md`/`AGENTS.md` if the change is architectural.

## P1 — Performance regression gating — ✅ DONE (2026-06-10)

All three sub-items landed together (commit "feat: add scheduled k6 performance gate with bulk
seeding and volume assertions"):

1. **Scheduled k6 CI workflow** — `.github/workflows/perf.yml` (nightly 02:00 UTC + dispatch,
   SHA-pinned actions, checksum-verified k6 install) runs `tests/k6/run-perf.sh`: throwaway
   PostgreSQL → DbMigrator → bulk seed → API in `UnsignedDevelopment` → `load.js`; k6 exits
   non-zero on any threshold breach (workflow red), and the `k6-summary` artifact is uploaded
   always.
2. **Data-volume seeding** — `tests/k6/seed/perf-seed.sql` (idempotent; 20k customers / 20k
   products / 20k orders with items, all owned by the k6 gateway identity so owner-scoped list
   queries see them). Schema drift is caught at PR time by `PerfSeedScriptTests` (real-PostgreSQL
   integration test: runs the script twice, asserts counts, idempotency, and owner scoping).
3. **Volume assertions** — list checks in `tests/k6/lib/{customers,products}.js` enforce
   `K6_MIN_LIST_ROWS` (default 1 locally; the gate sets 20 after seeding), so a fast-but-empty
   list response fails the run even when latency thresholds hold.

## P1 — Structurally guaranteed owner-policy evaluation — ✅ DONE (2026-06-10)

Landed (commit "feat: structurally verify owner-policy evaluation in the mediator pipeline"):
`OwnerOnlyPolicy.Authorize` records evaluation on a scoped `OwnerPolicyEvaluationTracker`;
non-create commands carry the `IOwnerAuthorizedMutation` marker (cohort completeness and
commands-only both convention-tested in `CqrsConventionTests`); `OwnerAuthorizationBehavior` in
the mediator pipeline asserts after a marked command completes non-exceptionally that the policy
was consulted — throwing in Development/Testing, logging an error in production (the mutation is
already persisted; failing the response would not undo it). Covered by behavior unit tests, a
real-Mediator pipeline test proving a policy-skipping handler is caught mechanically (the
done-when), and a DI-wiring assertion against the API host.

## P2 — Request-type feature toggles — ✅ DONE (2026-06-11)

Landed (commit "feat: add request-type feature toggles checked in the mediator pipeline"):
`[FeatureToggle("name")]` on command/query types; `FeatureToggleBehavior` (registered outermost,
before caching) refuses dispatch with `FeatureDisabledException` → 503 when configuration sets
`FeatureToggles:{name}` false — any configuration provider works, so a kill-switch needs no
redeploy. A missing entry means enabled, and `FeatureToggleConventionTests` enforces: request
types only, unique names, and an explicit appsettings entry per declared toggle (default state is
a reviewed decision). Covered by behavior unit tests, a real-Mediator refusal test, the 503
mapping test, and a pipeline-order assertion against the API host.
## P2 — Cache stampede protection (refresh-ahead) — ✅ DONE (2026-06-11)

Landed (commit "feat: add refresh-ahead cache stampede protection to CachingBehavior"):
cached values are stored in an envelope carrying `RefreshAfterUtc`
(= store time + `CacheDuration` − `CacheRefreshWindow`, the new explicit `ICacheable` member).
Inside the refresh window exactly one request per key recomputes inline via single-flight
(in-process `ConcurrentDictionary` claim) while concurrent requests keep the cached value — the
recompute deliberately runs on the caller, never a background scope, because owner-scoped keys
require the caller's gateway identity (a background refresh would risk cache poisoning).
Pre-envelope/unreadable entries degrade to a miss and are rewritten; the invalidation-tombstone
and null-skip semantics are preserved on the refresh path. Convention-tested: every cacheable
query's window is positive and smaller than its duration.
## P2 — Durable background-work run history — ✅ DONE (2026-06-11)

Landed (commit "feat: add durable job-run history for background work"): `job_runs` table
(migration 0003) + `IJobRunRecorder` in ServiceDefaults (Npgsql writer, fail-open — a history
write never breaks the job; conditional no-op without a database connection string;
`JobRuns:RetentionDays` opportunistic purge). The outbox processor aggregates
published/errored/retried/purged counts into one health row per `HealthRowIntervalMinutes` with
activity (`OutboxRunAggregator`, unit-tested; never per-message, idle windows emit nothing); the
payload-archive cleanup Function records each run with its deletion counts and a Failed row on
exception. Recorder behavior covered by integration tests against real PostgreSQL (start/complete,
single-shot, swallowed failures, retention purge); the Functions container now receives the
database connection in AppHost for exactly this purpose.
## P3 — Artifact and processing-stage capture — ✅ DONE (2026-06-11)

Landed (commit "feat: add correlation-bound artifact capture slot to payload capture"):
`IArtifactCaptureSink` in ServiceDefaults — channel `artifact`, direction `internal`, stages
`generated`/`intermediate`, a binary helper that base64-encodes with size metadata — wraps the
existing sink so artifacts land in the same archive/audit/entity-index scheme under the ambient
correlation id, bounded by `MaxPayloadBytes` with explicit truncation metadata. Failure policy is
per-channel like HTTP/Service Bus: `PayloadCapture:ArtifactFailureMode` (default FailOpen;
FailClosed propagates). Registered in DI; tests cover the blob round trip, binary encoding,
truncation, and both failure modes. The surface is ready before any artifact producer exists.
## P3 — Business-action audit taxonomy — ✅ DONE (2026-06-11)

Landed (commit "feat: stamp business-action taxonomy and verified identity onto audit rows"):
HTTP audit rows carry `action` (Create/Read/Update/Delete/StatusChange — verb-derived on request
rows, endpoint-override-aware on response rows via `WithAuditAction`, with the order
status/cancel routes overriding to StatusChange) and authenticated response rows carry the
verified `subject`/`tenantId`, making "all deletes by subject X" answerable from audit rows
alone. Overrides are convention-tested against the closed vocabulary; middleware tests assert the
stamped rows end-to-end through the real sink.
## P3 — Operational SQL query pack — ✅ DONE (2026-06-11)

Landed (commit "docs: add operational reporting query pack with schema-drift guard"):
`scripts/reporting/` holds reviewed, read-only support queries (outbox health + replay trail,
job-run history, orders by status over time, owner-scope distribution) with rules in its README
(read-only, owner-scope aware, indexed access). `ReportingQueryTests` executes every query against
the migrated schema so a column rename breaks the build, not an operator mid-incident.
## P3 — Module-scoped agent docs (only when the template grows)

A single root agent doc works at the current size. If the template ever grows into multiple
modules or bounded contexts, split agent guidance instead of letting the root doc bloat: the root
keeps vision, module priority (including any "highest-criticality, extra care" flags), build/test
commands, and an index; each module gets its own focused doc with business rules, command/event
inventory, critical test scenarios, and a pre-change safety checklist; shared-kernel-style code
gets a backward-compatibility policy (extend via overloads, never remove public surface) plus an
impact-analysis checklist. The doc-mirror convention test must extend to cover every new
agent-doc pair so the two trees cannot drift.

## Complexity-review port backlog (2026-06-12)

Source: a juice-vs-squeeze complexity review of this template (Karpathy lens — "don't simplify
for the sake of it", starter weight is partly the product), cross-checked against a derived
project's pruning experience. 76 findings; 37 adversarially verified. Tracked here so progress
survives sessions; each item is marked done with its commit, same discipline as the roadmap.

**P0 — live defect**
1. [x] Rate-limiting bundle — DONE (commit "fix: options-bind the rate limiter, drop the dead
   named policy, lift the limit for the perf gate"). Dead "fixed" policy deleted;
   `RateLimitingOptions` (validated, explicit appsettings defaults); partition key extracted to
   `ResolveRateLimitPartitionKey` with regression tests; `run-perf.sh` lifts `PermitLimit` since
   the whole load runs under one k6 identity — root cause of the 98.6%-error first nightly run.
   Follow-up in the same item: the de-throttled run then exposed a load-shape artifact — all 50
   order VUs convoyed on the ten setup-created products (create_order p95 20.12s; reads p95
   5.18ms) — fixed by widening the order pool from the bulk-seeded catalog in `load.js`.

**Ports (verified PORT verdicts)**
2. [x] Drop legacy DDL DEFAULTs — DONE (commit "refactor: complexity-review ports — DDL
   defaults, event-coverage convention, invalidator, IQuery inheritance"). Migration 0004 +
   23502 regression test.
3. [x] Event-coverage convention — DONE (same commit as item 2);
   `EveryDomainEventContract_MustBeCoveredByASubscriptionFilter` with empty publish-only allowlist.
4. [x] Serve-stale-on-refresh-failure — DONE (commit "feat: serve stale on refresh-ahead
   recompute failure; stamp replay metadata into capture").
5. [x] Drop the request-row `action` stamp — DONE (commit "refactor: small complexity-review
   ports — request-row action, ctor-event rationale, status length, toggle exemplar").
6. [x] Email/Client-Id/Issuer headers removed — DONE (commit "refactor!: sign amr as a
   first-class assertion field; drop the projected-header hash and unused identity headers").
7. [x] CacheInvalidator bare-key branch + convenience ctors deleted — DONE (same commit as
   item 2); CLAUDE.md WHY fixed in lockstep.
8. [x] `IQuery<TResult> : IRequest<TResult>` — DONE (same commit as item 2); dual declarations
   dropped from seven query files; tautological convention half removed, cohort-escape half kept.
9. [x] Replay metadata in capture — DONE (same commit as item 4); processor outbound capture
   and both Functions' inbound captures carry replay/replayCount.
10. [x] Rewrite the stale ChangeTracker rationale — DONE (same commit as item 5).
11. [x] `[FeatureToggle("order-placement")]` exemplar — DONE (same commit as item 5).
12. [x] `amr` signed as a first-class field; projected-header hash deleted — DONE (same commit
    as item 6). Tampered `X-Authenticated-Amr` now fails against the signature like scopes.
13. [ ] Shared Aspire E2E fixture: one distributed-app boot instead of five in
    `OutboxToServiceBusIntegrationTests`.
14. [ ] Compact `docs/ARCHITECTURE_REVIEW.md` to living state (~180 lines), archive narratives
    under `docs/reviews/`, collapse ROADMAP DONE bodies; sweep stale facts (review-skill score,
    "no mechanical rule" premises) — keep "Considered and rejected" verbatim.
15. [x] `.HasMaxLength(50)` on `Order.Status` — DONE (same commit as item 5).

**Simplify (lighter shape, verified)**
16. [x] `{RedactedPayload}` token + scan extended to `{Payload}` — DONE (commit "refactor:
    simplify-block ports — log token, probe skip, read-only reporting guard, KB pointers").
17. [x] Probe-route capture skip — DONE (same commit as item 16); exact-match four routes,
    pinned never-`/api` by test; capture-first decision amended in both doc mirrors.
18. [ ] Consistency suite: remove the embeddings stub + AST shingles (~1k lines); replace
    cohort-gating facts with synthetic-fixture extraction tests; emit report to file.
19. [x] Read-only reporting guard — DONE (same commit as item 16).
20. [x] KB pointers + verification-query schema guard — DONE (same commit as item 16).
21. [x] `docs/DERIVATION-PRUNING.md` — DONE (commit "docs: add the derivation-pruning
    discipline guide").

**Deferred (named triggers, decision-on-file)**
- Dead-letter-with-reason in Functions subscribers — trigger: first real handler logic.
- Doc-mirror generator — trigger: mirror set grows beyond root pair + skills.
- Per-stage capture-sink failure isolation — trigger: any deployment opting the HTTP channel
  into FailClosed.

## Considered and rejected

Recorded so future sessions do not re-propose them:

- **MediatR / AutoMapper / repository pattern / in-process background task queue** — all conflict
  with documented prohibitions or existing mechanisms (custom mediator, explicit mapping, DbContext
  directly, transactional outbox for anything that must survive a restart).
- **Production infrastructure as code (Bicep/azd)** — maintainer decision (2026-06-10): not wanted;
  deployment topology is owned by the hosting environment, Aspire remains the only orchestration
  path in this repo.
- **WORM/immutability policy on audit blobs as a roadmap item** — cannot be expressed in this
  repo's scope (the local storage emulator does not enforce immutability, and there is no IaC
  here). Recorded instead as an accepted limitation with deployer guidance in
  `docs/ARCHITECTURE_REVIEW.md` (2026-06-10).
- **Spike isolation convention (`spikes/` folder)** — maintainer decision (2026-06-10): not wanted;
  experiments go through normal branches/worktrees and review.
- **Client-IP extraction chains in middleware** — the API runs behind a trusted gateway; the
  gateway owns client network identity.
- **List-query caching** — already deliberately excluded (no pattern-based invalidation in
  `IDistributedCache`); revisit only with a versioned-namespace design.
- **TypeScript client codegen** — marginal for an API-only template; OpenAPI output already serves
  contract consumers.
- **Request-row audit `action` stamp** — captured before routing, the verb-derived value was wrong
  on exactly the override routes and duplicated `method`; the response row is the authoritative
  action carrier (complexity review, 2026-06-12).
