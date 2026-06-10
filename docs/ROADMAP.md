# Improvement Roadmap

Last updated: 2026-06-10

Forward-looking capability backlog for this template. It complements `docs/ARCHITECTURE_REVIEW.md`
(which tracks findings and fixes in the existing code) by tracking capabilities the template should
gain. Items follow the repo philosophy: mechanical rules over architectural taste, convention-test
enforcement wherever a rule can be made deterministic.

When an item lands: mark it done here with the commit/PR, add regression/convention tests with the
implementation, and update `CLAUDE.md`/`AGENTS.md` if the change is architectural.

## P1 — Performance regression gating

The k6 suite (`tests/k6/`) is solid but only runs when someone remembers to run it, against a
near-empty database. Performance regressions currently ship silently.

1. **Scheduled k6 CI workflow.** Nightly GitHub Actions workflow modeled on `dast.yml`'s shape:
   boot throwaway PostgreSQL, run DbMigrator, start the API in `GatewayIdentity:Mode=UnsignedDevelopment`,
   run `tests/k6/load.js`, fail the run on any threshold breach, and upload the k6 summary as an
   artifact for trending. Scheduled (not per-PR) so it never slows the inner loop.
   *Done when:* a threshold regression turns the workflow red and the summary artifact is downloadable.
2. **Data-volume seeding for load runs.** `load.js` setup currently seeds ~10 customers/products, so
   list, pagination, and index paths are exercised against an almost empty database — the cases that
   actually regress (missing index, OFFSET cliff) are invisible. Add a bulk seed step (tens of
   thousands of rows across customers/products/orders) that runs after migrations and before k6.
   *Done when:* the scheduled run executes against the seeded volume and per-endpoint thresholds still pass.
3. **Volume assertions in k6 checks.** Checks currently assert status and shape
   (`data is array`) but not size, so a fast-but-empty response passes. With seeding guaranteeing
   known minimum volumes, list-endpoint checks should assert minimum row counts.
   *Done when:* an accidentally-empty list response fails the load run even if it is fast.

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

## P2 — Durable background-work run history

`OutboxProcessor` and the timer-triggered cleanup function run dark — their history exists only in
logs. Add a small job-run record (job name, started/completed timestamps, outcome, summary counts
such as published/errored/deleted) written by timer jobs per run and by the outbox processor as
periodic aggregate health rows (not per message). Gives support a queryable "what did the
background work actually do" trail that matches the payload-capture philosophy of jumping from
symptom to evidence.

## P2 — Tamper-proof audit retention (WORM)

The audit/archive blob trail is the system's evidence record, yet any credential with blob-delete
rights (including the cleanup function, or a bug in it) can destroy it before retention expires.
Apply a time-based immutability (WORM) policy on the `audit/` container (and consider `archive/`)
aligned with `PayloadCapture:RetentionDays`, so deletion is impossible inside the retention window
and the cleanup function only ever succeeds on blobs past it. Document the policy in the payload
capture docs; add an AppHost/infra note since the policy is set on the storage resource, not in code.

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

## P3 — Production infrastructure as code

Aspire is the local orchestration path; production topology (API, Functions, PostgreSQL, Service
Bus, Redis, storage, monitoring) has no IaC story. Add Bicep (or azd) templates with two parameter
sets — dev-friendly and production-locked — including the storage immutability policy from the WORM
item and DbMigrator-as-a-job sequencing before API rollout.

## P3 — Spike isolation convention

A `spikes/<topic>/` convention for throwaway experiments: standalone solutions excluded from the
root build, lock-file policy, and CI; documented rule that spikes are disposable and graduate into
`src/` only via a normal reviewed change. Gives experiments a sanctioned home instead of leaking
half-finished code into the main tree.

## Considered and rejected

Recorded so future sessions do not re-propose them:

- **MediatR / AutoMapper / repository pattern / in-process background task queue** — all conflict
  with documented prohibitions or existing mechanisms (custom mediator, explicit mapping, DbContext
  directly, transactional outbox for anything that must survive a restart).
- **Client-IP extraction chains in middleware** — the API runs behind a trusted gateway; the
  gateway owns client network identity.
- **List-query caching** — already deliberately excluded (no pattern-based invalidation in
  `IDistributedCache`); revisit only with a versioned-namespace design.
- **TypeScript client codegen** — marginal for an API-only template; OpenAPI output already serves
  contract consumers.
