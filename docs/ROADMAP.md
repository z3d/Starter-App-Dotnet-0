# Improvement Roadmap

Last updated: 2026-06-12 (rev 5: original roadmap fully delivered — DONE bodies collapsed to
one-liners, full narratives in the git history of this file; complexity-review backlog appended
and being executed. Earlier revisions are in git history.)

Forward-looking capability backlog for this template. It complements `docs/ARCHITECTURE_REVIEW.md`
(which tracks findings and fixes in the existing code) by tracking capabilities the template should
gain. Items follow the repo philosophy: mechanical rules over architectural taste, convention-test
enforcement wherever a rule can be made deterministic.

When an item lands: mark it done here with the commit/PR, add regression/convention tests with the
implementation, and update `CLAUDE.md`/`AGENTS.md` if the change is architectural.

## P1 — Performance regression gating — ✅ DONE (2026-06-10)

All three sub-items landed together (commit "feat: add scheduled k6 performance gate with bulk seeding and volume assertions").

## P1 — Structurally guaranteed owner-policy evaluation — ✅ DONE (2026-06-10)

Landed (commit "feat: structurally verify owner-policy evaluation in the mediator pipeline"): `OwnerOnlyPolicy.Authorize` records evaluation on a scoped `OwnerPolicyEvaluationTracker`.

## P2 — Request-type feature toggles — ✅ DONE (2026-06-11)

Landed (commit "feat: add request-type feature toggles checked in the mediator pipeline"): `[FeatureToggle("name")]` on command/query types; `FeatureToggleBehavior` (registered outermost.

## P2 — Cache stampede protection (refresh-ahead) — ✅ DONE (2026-06-11)

Landed (commit "feat: add refresh-ahead cache stampede protection to CachingBehavior"): cached values are stored in an envelope carrying `RefreshAfterUtc`.

## P2 — Durable background-work run history — ✅ DONE (2026-06-11)

Landed (commit "feat: add durable job-run history for background work"): `job_runs` table (migration 0003) + `IJobRunRecorder` in ServiceDefaults (Npgsql writer, fail-open — a history.

## P3 — Artifact and processing-stage capture — ✅ DONE (2026-06-11)

Landed (commit "feat: add correlation-bound artifact capture slot to payload capture"): `IArtifactCaptureSink` in ServiceDefaults — channel `artifact`, direction `internal`, stages.

## P3 — Business-action audit taxonomy — ✅ DONE (2026-06-11)

Landed (commit "feat: stamp business-action taxonomy and verified identity onto audit rows"): HTTP audit rows carry `action` (Create/Read/Update/Delete/StatusChange — verb-derived on request.

## P3 — Operational SQL query pack — ✅ DONE (2026-06-11)

Landed (commit "docs: add operational reporting query pack with schema-drift guard"): `scripts/reporting/` holds reviewed, read-only support queries (outbox health + replay trail.

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
13. [x] Shared Aspire E2E fixture — DONE (commit "test: boot the distributed app once for the
    Aspire E2E collection"); 5 boots → 1, verified by the CI aspire job (local Docker was
    unavailable; see commit message).
14. [x] Review-doc compaction — DONE (commit "docs: compact the architecture review to living
    state; archive the narrative history"). Living doc ~100 lines; archive verbatim under
    docs/reviews/; DONE bodies collapsed; stale review-skill score and skill-authoring
    parenthetical swept (both mirrors).
15. [x] `.HasMaxLength(50)` on `Order.Status` — DONE (same commit as item 5).

**Simplify (lighter shape, verified)**
16. [x] `{RedactedPayload}` token + scan extended to `{Payload}` — DONE (commit "refactor:
    simplify-block ports — log token, probe skip, read-only reporting guard, KB pointers").
17. [x] Probe-route capture skip — DONE (same commit as item 16); exact-match four routes,
    pinned never-`/api` by test; capture-first decision amended in both doc mirrors.
18. [x] Consistency suite cut — DONE (commit "refactor: cut the consistency suite's stub
    layers; pin extraction with synthetic fixtures"). Embeddings + AST shingles removed
    (~1.1k lines); synthetic-fixture extraction tests pin the fingerprint machinery; reports
    land in docs/_local/consistency-*.txt with a consumption note in the testing skill.
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
