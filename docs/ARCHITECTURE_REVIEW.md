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
| [2026-09-02 whole solution](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md) ([rendered](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.html)) | Twenty-two findings, three dismissed; resolved and validated 2026-09-03 |
| [2026-09-05 targeted review](reviews/ARCHITECTURE_REVIEW-2026-09-05.md) | Eight findings, **all resolved 2026-09-06**; regression and validation evidence in the record |
| [2026-09-06 fix review](reviews/ARCHITECTURE_REVIEW-2026-09-06.md) | Independent review of the 2026-09-06 fix commit; fourteen residuals, twelve fixed the same day, two recorded with triggers |

## Overview

A .NET 10 Clean Architecture template (CQRS, DDD, transactional outbox, Aspire) over a deliberately
small e-commerce domain. It is agent-maintained, so its weight is a design stance: patterns are the
pedagogy and convention tests are the product. A 2026-06-12 complexity review confirmed that stance
and pruned what failed it; its backlog was fully delivered and is preserved in the git history of
the retired `docs/ROADMAP.md`.

**Score: 8.1/10, last set 2026-09-06.** The score is self-assessed by the maintaining agents with
no external validator and no fixed rubric; treat it as a maintenance log, not an audit. It dips on
discovery and recovers only with verified fixes. The 2026-09-05 review found eight defects, two of
them reopening earlier closures whose tests had checked configuration shape rather than behaviour;
all eight were fixed on 2026-09-06 with failing-before regressions and an independent re-read of
the diff, so the net move from 8.0 is +0.1 rather than a full recovery. Verifiable snapshot as of
2026-09-06 (re-verify, don't trust): 9 command handlers, 7 query handlers, every request validated
by convention, roughly 770 tests green plus the AppHost suite, 39 deterministic DAST runner
regressions in the DAST workflow, nightly k6 gate and DAST scan passing on `main`.

## Strengths

Convention-enforced boundaries (110+ mechanical rules including supply chain, doc mirror, and
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

- **OPEN — bearer tokens are not sender-constrained.** The retired gateway assertion was bound to
  method and path with a ~150s lifetime; an IdP bearer token is valid for any endpoint in its
  audience until expiry. Mitigated by short lifetimes and strict audience validation. Fix: DPoP
  (RFC 9449) or mTLS-bound tokens (RFC 8705); Keycloak supports both locally, Entra's narrower
  support constrains the production IdP choice. Trigger: before any production deployment on an
  untrusted network, or the first token observed outside its intended client.

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
  out the healthy-API fact. The shared E2E fixture gates every fact on API readiness (5-minute
  budget); subscriber-dependent facts opt in via `EnsureFunctionsReadyAsync()` (10-minute budget).
  Since 2026-09-06 the outbox end-to-end fact awaits that gate itself instead of relying on test
  ordering, so a recurrence is a genuine boot timeout. Raise the timeout if it recurs.
- **`IArtifactCaptureSink` has no producer.** The slot shipped ahead of any producer on
  2026-06-12. Wire the first artifact producer through it; if a year passes with none, reopen the
  keep decision.

## Deferred with named triggers

- **Doc-mirror generator.** Trigger: the mirror set grows beyond the root pair plus skills.
- **Per-stage capture-sink failure isolation.** Trigger: a deployment opts the HTTP channel into
  FailClosed, or duplicate archive rows become a support problem. Under ServiceBus FailClosed an
  entity-index failure after the archive append rethrows, and the subscriber's in-process retry
  appends the same archive line again (up to six per delivery, never a loss); the
  [2026-09-06 record](reviews/ARCHITECTURE_REVIEW-2026-09-06.md) has the analysis.
- **Module-scoped agent docs.** A single root agent doc works at the current size. Trigger: the
  template grows into multiple modules. Then the root keeps vision, build and test commands, and an
  index; each module gets its own doc with business rules, command and event inventory, and a
  pre-change checklist; the doc-mirror convention test extends to every new pair.
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
