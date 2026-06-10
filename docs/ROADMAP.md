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

## P1 — Structurally guaranteed owner-policy evaluation

Convention tests enforce that mutation handlers *inject* `IOwnerOnlyPolicy`, but injection is not
invocation — a handler can take the dependency and never call it, and no mechanical rule catches
that today. Owner checks must stay in handlers (only the loaded aggregate knows its persisted
owner; see CLAUDE.md), so the fix is not endpoint metadata. Design direction: make the scoped
policy record that it was evaluated, and add a mediator pipeline behavior that, after a mutation
handler for an owner-scoped aggregate completes successfully, asserts the policy was consulted
(fail loudly in Development/Testing; log/metric in production).
*Done when:* a mutation handler that skips the owner check fails a test mechanically, not by review.

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

## P2 — Event-contract shape snapshot guard

Convention tests guard event contract *names* (non-empty, unique, referenced by valid subscription
filters) but nothing guards the serialized *shape* of each versioned event. Renaming a property on
an event class silently changes the wire payload under the same contract id (e.g.
`order.created.v1`), breaking Function subscribers and diverging from previously archived outbox
payloads — the exact failure the versioned-contract convention exists to prevent. Add snapshot
tests: one pinned canonical JSON fixture per event contract; the test serializes a representative
instance with the outbox's serializer settings and diffs it against the fixture. Any difference
fails the build until the fixture is deliberately updated — which forces the author to decide
between a compatible change and a new `.v2` contract.
*Done when:* renaming a property on an existing domain event fails CI with a readable diff.

## P2 — Operator replay path for failed messages

The failure half of eventing keeps evidence but has no sanctioned recovery: errored outbox rows
are retained yet only ever skipped on subsequent polls, and expired subscription messages
dead-letter by design — in both cases recovery today means ad-hoc database or queue surgery. Add
an operator replay capability: (a) a sanctioned way (admin endpoint, Function, or console verb) to
reset an errored outbox row for re-publishing once the underlying fault is fixed, and (b) a
documented dead-letter replay step for subscription DLQs — re-submit the dead-lettered message to
the topic, with the archived payload (already captured) as the fallback source. Replays must flow
through the normal pipeline — payload capture, audit, and subscribers' replay tolerance — never a
side channel; replayed messages should carry a marker (application property) so audit can
distinguish replay from first delivery.
*Done when:* an intentionally failed event can be recovered end-to-end in an integration test
without hand-written SQL or portal surgery.

## P2 — Incident knowledge base (diagnosis-side companion to the replay path)

Recurring async-failure investigations (dead-lettered messages, errored outbox rows, capture
failures) leave no durable, queryable record — each incident starts the diagnosis from zero. Add a
machine-readable knowledge base under `docs/investigations/` (e.g. one `knowledge-base.json` per
failure domain) recording: known failure patterns, each with a default action and a pinned
verification query (logs/SQL) that proves the pattern applies before the action is taken; known
code defects with their fix commit and deployment status; and a timestamped history of
investigations with actions taken and new patterns learned. Guardrail to keep it honest: any entry
recording a code defect must link either to a fix commit or to an accepted-limitation entry in
`docs/ARCHITECTURE_REVIEW.md` — the knowledge base must never become a place where known bugs
quietly age without a decision.
*Done when:* a second occurrence of a known failure pattern is resolved by following the recorded
action and verification query, without re-deriving the diagnosis.

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
