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

## P2 — Request-type feature toggles

A `[FeatureToggle("name")]` attribute on command/query types, checked centrally in the mediator
before dispatch, driven by configuration. Disabled requests return a well-defined ProblemDetails
response (e.g. 503 with a feature-disabled code), never reach handlers, and require no redeploy to
flip. Convention tests enforce: attribute lives on request types (not handlers), names are unique
and non-empty, and every toggle name has a configuration entry. This gives kill-switch and
dark-launch capability with a single mechanical rule, no third-party flag service.

## P2 — Cache stampede protection (refresh-ahead)

`CachingBehavior` is plain get/check/store: when a hot key expires under load, every concurrent
request misses and executes the handler simultaneously. Extend `ICacheable` with a refresh-ahead
window: within the window, serve the cached value and trigger a single background refresh
(single-flight per key). Convention tests: refresh window must be positive and smaller than the
cache duration. Owner-scoped key semantics must be preserved exactly.

## P2 — Event-contract shape snapshot guard — ✅ DONE (2026-06-10)

Landed (commit "test: pin event-contract wire shapes with snapshot fixtures"):
`EventContractSnapshotTests` renders each event through the real `OutboxMessage.Create` path,
normalizes only volatile timestamp values, and diffs against pinned fixtures in
`src/StarterApp.Tests/Contracts/snapshots/{contract}.json`. Completeness is mechanical: every
`IDomainEvent` must have a representative instance and fixture, and orphan fixtures fail.
Deliberate updates run `UPDATE_EVENT_SNAPSHOTS=1 dotnet test --filter EventContractSnapshot`,
forcing the compatible-change-vs-new-`.v2` decision into review. Verified: a property rename
fails with a readable pinned-vs-actual diff (the done-when).

## P2 — Operator replay path for failed messages — ✅ DONE (2026-06-10)

Landed (commit "feat: add sanctioned outbox replay verb and dead-letter replay runbook"):
(a) `DbMigrator` gains the `replay-outbox` verb (`--id <guid>` | `--all-errored`) — resets only
unprocessed errored rows (clears error + stale claim, restores retry budget, stamps
`replay_count`/`replayed_on_utc`, migration 0002); the processor marks replayed publishes with
`Replay`/`ReplayCount` application properties. `OutboxMessage.ResetForReplay` carries the same
semantics for in-process use, with a parity test keeping the SQL and entity representations in
sync. (b) `docs/runbooks/event-replay.md` documents the subscription-DLQ re-submit procedure with
archived payloads as the fallback source. The done-when is covered by `OutboxReplayTests`:
an intentionally errored event is recovered end-to-end through the verb with no hand-written SQL.

## P2 — Incident knowledge base — ✅ DONE (2026-06-10)

Landed (commit "docs: scaffold the incident knowledge base with mechanical guardrails"):
`docs/investigations/` holds one `knowledge-base.json` per failure domain (schema + rules in the
README there), seeded with the outbox/DLQ domain — one real pattern (FailClosed archive outage
pauses the batch) and reusable verification queries. `KnowledgeBaseConventionTests` enforces the
shape mechanically: every pattern needs a defaultAction AND a verification query, and every
recorded defect must link a fixCommit or an accepted-limitation reference in
`docs/ARCHITECTURE_REVIEW.md` — known bugs cannot quietly age in the file. The done-when
(second occurrence resolved from the recorded action + verification) is the operating procedure
the README binds investigators to; the scaffold and guardrails are what the repo can enforce.
## P2 — Durable background-work run history

`OutboxProcessor` and the timer-triggered cleanup function run dark — their history exists only in
logs. Add a small job-run record (job name, started/completed timestamps, outcome, summary counts
such as published/errored/deleted) written by timer jobs per run and by the outbox processor as
periodic aggregate health rows (not per message). Gives support a queryable "what did the
background work actually do" trail that matches the payload-capture philosophy of jumping from
symptom to evidence.

## P3 — Artifact and processing-stage capture

Payload capture is boundary-only (HTTP in/out, Service Bus in/out). Once the template generates
artifacts — rendered documents, exports, batch files — "the payload that arrived" and "the artifact
we produced" are different evidence. Add a correlation-bound capture slot for generated artifacts
and significant intermediate transformations, reusing the existing archive/audit/entity-index
scheme and failure-mode policy. Keep the API surface ready even before a concrete artifact
producer exists.

## P3 — Business-action audit taxonomy

Audit rows record operation metadata (method, path, correlation) but no business semantics. Add a
small action taxonomy (Create/Update/Delete/Read/StatusChange, derived from method + route with
per-endpoint override) stamped onto audit rows, so support and compliance queries can ask "all
deletes by subject X" without parsing routes.

## P3 — Operational SQL query pack

A versioned, reviewed set of support/ops queries under `scripts/reporting/*.sql` (or `docs/reporting/`):
orders by status over time, outbox backlog and errored counts, payload-capture volume per day,
owner-scope distribution. Cheap to add, prevents every incident from reinventing the same SQL, and
gives agents a sanctioned place to add new support queries with review.

## P3 — Module-scoped agent docs (only when the template grows)

A single root agent doc works at the current size. If the template ever grows into multiple
modules or bounded contexts, split agent guidance instead of letting the root doc bloat: the root
keeps vision, module priority (including any "highest-criticality, extra care" flags), build/test
commands, and an index; each module gets its own focused doc with business rules, command/event
inventory, critical test scenarios, and a pre-change safety checklist; shared-kernel-style code
gets a backward-compatibility policy (extend via overloads, never remove public surface) plus an
impact-analysis checklist. The doc-mirror convention test must extend to cover every new
agent-doc pair so the two trees cannot drift.

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
