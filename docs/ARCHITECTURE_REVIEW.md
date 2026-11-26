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
| [2026-11-26 fixed findings](reviews/ARCHITECTURE_REVIEW-2026-11-26-fixed-findings.md) | 64 findings fixed between October and November 2026 that had stayed under Open findings; moved here unchanged |

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

- **OPEN 2026-11-22: the published images are rebuilt, not the scanned ones.** `publish-images.yml` builds each image again after CI passes (and a manual dispatch builds with no scan at all), and the Functions `apt-get install --only-upgrade` and the web image's `apk upgrade --no-cache` resolve against the live archive at build time, so the pushed image can differ from the one Trivy passed. Closing it means CI hands its scanned images to the publish job (registry staging tag or artifact, push runs only) and the publish job retags them, or the publish job scans what it builds before pushing. Not changed with the scan fix because it reshapes both workflows.
- **2026-11-03: DAST builds the API before starting its readiness deadline.** A derived project's 1 November run expired after two minutes with an empty API log while `dotnet run` still owned the build. Compilation now finishes in the foreground and its failure is reported directly; the background process uses `--no-build --no-restore`, so the existing two-minute bound measures API startup. The deterministic DAST runner regressions pass; the historical run's build duration is unconfirmed.

The eight findings from the [2026-09-05 review](reviews/ARCHITECTURE_REVIEW-2026-09-05.md#resolution--2026-09-06)
are resolved, with failing-before/passing-after regressions and final validation in the record.

Open from the [2026-09-27 review](reviews/REVIEW-2026-09-27-fresh-eyes.md#follow-up-2026-09-27) (the rest were fixed or accepted the same day; see the follow-up table):

- None: every finding is fixed, accepted (#10, probes behind the gateway), or dismissed (#5, #15's gitleaks half).

- **OPEN — bearer tokens are not sender-constrained.** The retired gateway assertion was bound to
  method and path with a ~150s lifetime; an IdP bearer token is valid for any endpoint in its
  audience until expiry. Mitigated by short lifetimes and strict audience validation. Fix: DPoP
  (RFC 9449) or mTLS-bound tokens (RFC 8705); Keycloak supports both locally, Entra's narrower
  support constrains the production IdP choice. Trigger: before any production deployment on an
  untrusted network, or the first token observed outside its intended client.

Open from the 23 October 2026 audit of a project derived from this template, each checked against this repository's code before it went in (severity is the one checked here):

### Found by the read-only review of 10 November 2026

Found by reading the code. The items marked FIXED were fixed the same day, with the tests named; the rest are open and were not reproduced.

- **A product edit overwrites live stock with the client's stale number.** `UpdateProductCommand.cs:51-55` computes `command.Stock - product.Stock` from a fresh load and the `PUT` takes no `If-Match` or expected version, so `xmin` cannot catch a value the client read earlier. Read stock 10, an order takes 3, a `PUT` that only fixes the description sends 10 and adds 3 units that do not exist. Derived projects copy this shape.
- **Order reads round GST to 2 places whatever the currency.** `GetOrderByIdQuery.cs:35-37,51-54`, `GetOrdersByCustomerQuery.cs`, `GetOrdersByStatusQuery.cs` use `round(unit_price * gst_rate, 2)`; `OrderItem.cs:75-78` rounds to `Money.MinorUnits`. JPY 1005 at 10%: the POST and the event say 1106, the GET says 1105.50.
- **Domain events carry no tenant or owner.** `OrderCreatedDomainEvent`, `OrderStatusChangedDomainEvent` hold only ids, the event is the wire contract, and no convention test requires a tenant on an event. A subscriber that writes a projection has to look the row up without knowing its tenant.
- **A commit-unknown retry inside a plain `SaveChanges` becomes a 409.** `UpdateOrderStatusCommand.cs:47`, `CancelOrderCommand.cs:38`: the replayed batch matches 0 rows on `xmin`, so the client gets a 409 for a change that landed. Only `CreateOrder` re-checks.

The first, second and last of the open items are in the sample domain, which a derived project removes at its first module; they matter as the shape a module copies.

### Found while porting the assistant (21 November 2026)

- **OPEN: the assistant has not answered a live call here.** No key is configured where it was ported, so the prompt in `AssistantPrompt.cs` (the sample's system text, notes and two specific tools) has not been read by a model, and the recorded responses the integration tests replay were shaped by hand from a derived project's captures, not captured under this prompt. What is proven today: the turn route sends the configured model the prompt, the tools and the route list and carries a recorded reply back (`AssistantApiTests`); the browser loop, the cards and the forms (`assistant.test.ts`, `assistant-panel.test.tsx`, `assistant-home.test.tsx`). Not proven: that a model given this prompt finds, asks and proposes as intended.
- **OPEN: a card asks for a field the API requires as if it were optional.** `CreateCustomerCommand`'s name and email are non-nullable strings, but the OpenAPI document lists no `required` fields for it, so the catalogue marks neither as required and the card lets the person confirm without them; the API's validator then refuses with a 400 the card shows. Seen on the card for `POST /api/v1/customers` in a component test, not in a browser. Cause unconfirmed: the document generator may need the `required` keyword or a schema transformer.

### Found while working the backlog (21 November 2026)

- **OPEN: the DAST active scan was killed on this machine** (ZAP exit 137, most likely memory in the shared 5 GB Podman VM with other stacks running; unconfirmed). The run got as far as the active scan as `Testing`: the export of the OpenAPI document, the include-path check (12 `/api/` routes), the import of 20 URLs and the spider all worked. The full scan as `Testing` is unproven until a run on a quieter machine or on CI.
- **Mutation score, first run (Stryker.NET 5.1.0, unit tests in `StarterApp.Tests.Domain` only):** `StarterApp.SharedKernel` 76.19% (48 killed, 10 survived, 5 with no test reaching them), `StarterApp.Domain` 67.15% (139 killed, 45 survived, 23 not reached; 17 mutants did not compile and are not counted). The surviving mutants are in the HTML reports `tests/mutation/run-mutation.sh` writes; none is fixed yet. Re-run after a test change with `tests/mutation/run-mutation.sh`; the weekly `Mutation` workflow runs it when CI is on.

### Found while hardening tenant isolation (23 November 2026)

- **OPEN: the deployed logins are unproven against a live server.** Publish mode now makes only the migrator an Entra administrator and gives the API and the worker identities the migrator turns into principals with `pgaadauth_create_principal_with_oid`; that the administrator may call it and grant the roles, and that the identity's token signs in as that principal, is checked at the first deployment, not here. `DatabaseLoginGuard` refuses to start a process whose login could bypass the policies.

### Found while moving the worker in process (25 November 2026)

- **OPEN: the published worker's access to Azure Managed Redis is unproven.** The AppHost gives the worker `WithReference(redis)` as it gives the API, which in publish mode should assign its identity a role on the cache; it was not deployed or read in the generated Bicep here.

## Accepted with a trigger

- **Folder-only Clean Architecture (2026-06-09).** `Domain` is compiler-enforced; the handlers
  and `Infrastructure` are folders inside one project, `StarterApp.Application` (apart from the
  web host since 2026-11-25, so the worker can run them), and that boundary is
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
- **Recent-MFA check for high-risk actions.** `SecuredBy2Fa()` asks only whether the session's
  sign-in used a second factor (`amr` contains `mfa`). The identity provider's session bounds how
  long that holds (the dev realm: 30 minutes idle, 10 hours at most); nothing asks how recently.
  That is right for the template's policy, a second factor on every write, where a freshness rule
  would send a user back to MFA every few minutes. Trigger: a derived project names an action that
  needs a fresh second factor (role grants, payout or bank-detail changes, bulk export, breach
  notification). Then add a separate `RequireRecentMfa(TimeSpan)` filter beside `SecuredBy2Fa()`,
  map `auth_time` in `JwtIdentityMiddleware` next to `amr`, and answer a stale or missing value
  with the RFC 9470 challenge carrying `max_age`, so the client re-authenticates and retries.
  Entra v2 and Auth0 access tokens carry no `auth_time` by default, so the IdP profile must add
  it, and a token without it fails closed. `SecuredBy2Fa()` itself stays as it is.
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
