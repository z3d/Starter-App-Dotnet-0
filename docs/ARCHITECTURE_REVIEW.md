# Architecture Review

Living state only. Narrative review history (every dated session note, resolved-finding table,
and dismissed-false-positive record through 2026-06-12) is preserved verbatim in
[docs/reviews/ARCHITECTURE_REVIEW-2026-06-archive.md](reviews/ARCHITECTURE_REVIEW-2026-06-archive.md).
Skeptics verifying "was this already dismissed?" consult the archive; this file answers "what is
true and open right now".

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices
across a deliberately small e-commerce sample domain (Products, Customers, Orders) with Aspire
orchestration and PostgreSQL. The template is agent-maintained; its heavy machinery is a
deliberate design stance (patterns are the pedagogy, convention tests are the product), not
accidental weight — a 2026-06-12 juice-vs-squeeze complexity review confirmed the stance and
pruned what failed it (see `docs/ROADMAP.md`, complexity-review backlog).

**Score: 8.2/10** — last independently re-scored 2026-06-09 (strict production scale).
**Read this before trusting the number**: the score is self-assessed by the maintaining agents
(Claude and Codex across sessions) with no external human validator and no fixed rubric; treat it
as a maintenance log, not an audit. The historical self-graded 9.7 was stale/monotonic — the
archive retains it for provenance only. Held below 9 by the folder-only Clean Architecture
deferral and the accepted limitations below, not by open runtime defects.

Verifiable snapshot (re-verify, don't trust): 9 command handlers, 7 query handlers, every
command/query validated (convention-enforced), 0 CQRS violations, full suite ~680 tests green
plus AppHost integration tests; the nightly k6 perf gate and DAST scan both pass on `main`.

## Strengths (compressed — the archive carries the full analysis)

Convention-enforced boundaries (110+ mechanical rules incl. supply-chain, doc-mirror, event
coverage); rich DDD aggregates with client-generated v7 ids for creation-event aggregates;
strict CQRS (EF commands / Dapper reads); transactional outbox with claim/salvage, per-cause
retry budgets, replay verb + runbook, and pinned event-contract snapshots; full payload
capture/audit posture with per-channel failure policy, owner-scoped redaction rules, and
correlation-bound artifact slot; signed gateway identity with every projected value signed
individually; owner-scoping enforced in predicates, policy, cache keys, and rate-limit
partitions, with policy invocation structurally verified in the mediator pipeline; refresh-ahead
caching with serve-stale-on-error; job-run history; incident knowledge base + reporting pack,
both schema-guarded; supply-chain hardening (CPM + locked-mode, digest-pinned images, SHA-pinned
actions, gitleaks, Dependabot, CodeQL); k6 perf gate (nightly, seeded, volume-floored) and ZAP
DAST, both green.

## Open Findings

Decisions / watch-items / explained deferrals — no open runtime defects.

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
- **Gateway-assertion replay window — ACCEPTED (threat-model decision).** A valid assertion
  replays within its ~150s lifetime (no `jti`/nonce store). Accepted under trusted-gateway + TLS:
  a replay can only repeat the exact same authorized call. Fix-when-needed: gateway-emitted `jti`
  checked against the shared Redis with TTL = token lifetime (then hard-requires shared Redis
  across replicas). Revisit on zero-trust networks or regulated contexts.
- **Assertion does not sign body or query string — ACCEPTED (threat-model decision).** Signature
  binds method + path + identity + scopes + amr + expiry; body/query tampering inside the
  encrypted gateway→API hop is out of the threat model (TLS covers transit). Fix-when-needed:
  sign the query string (cheap) and a body hash (costly — full-body buffering both sides).
- **Audit/archive blobs are not WORM-protected — ACCEPTED (2026-06-10, deployer guidance).**
  Anything with blob-delete rights can destroy audit records inside the retention window; the
  repo cannot express an immutability policy (emulator doesn't enforce it; no IaC by decision).
  Deployers: apply a time-based immutability policy aligned with `PayloadCapture:RetentionDays`.
- **Product create retry-idempotency under commit ambiguity — ACCEPTED (2026-06-10).**
  `CreateProductCommandHandler` has no natural key and a DB-generated int id, so a
  commit-succeeded-but-ack-lost retry can insert a duplicate row. The fix (client-generated v7 id
  or a unique business key) is a deliberate stakeholder decision left open; Customer create is
  idempotent via its owner-scoped unique email. Residual test gaps recorded in the archive.
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
