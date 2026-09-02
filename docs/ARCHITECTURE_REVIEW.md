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
[docs/reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md).
Skeptics verifying "was this already dismissed?" consult the archive; this file answers "what is
true and open right now".

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices
across a deliberately small e-commerce sample domain (Products, Customers, Orders) with Aspire
orchestration and PostgreSQL. The template is agent-maintained; its heavy machinery is a
deliberate design stance (patterns are the pedagogy, convention tests are the product), not
accidental weight — a 2026-06-12 juice-vs-squeeze complexity review confirmed the stance and
pruned what failed it (see `docs/ROADMAP.md`, complexity-review backlog).

**Score: 6.9/10** — reduced 2026-09-02 after a whole-solution review found five medium
cross-cutting gaps (error responses shed every security header and the correlation-id echo; a
health check mints an Azure credential per probe on two unthrottled routes; route-path entity
references skip the sensitive-name screen; four raw IL byte loops in convention tests; the
emulator reset script cannot run on macOS) and seventeen lows, none touching data integrity or
identity (was 7.1 after the 2026-08-04 runtime-hardening review found one high and four
medium runtime gaps, 7.7 after the same-day post-IdP review and 8.0 after the gateway→OIDC
identity conversion, independently re-scored 2026-06-09 on a strict production scale). The
conversion trades a distinctive strength (individually-signed gateway assertions) for zero-trust
posture (the API verifies the caller's own credential, asymmetric JWKS, no shared secrets); the net
dip is the open sender-constraining finding plus the nine 2026-08-04 findings and the five
2026-09-02 findings below. Recover the 1.1 review dip when those fourteen fixes and their
regression tests land; revisit the remaining identity
posture when DPoP/mTLS-binding lands or the replay finding is re-accepted with evidence.
**Read this before trusting the number**: the score is self-assessed by the maintaining agents
(Claude and Codex across sessions) with no external human validator and no fixed rubric; treat it
as a maintenance log, not an audit. The historical self-graded 9.7 was stale/monotonic — the
archive retains it for provenance only. Held below 9 by the open findings, folder-only Clean
Architecture deferral, and accepted limitations below.

Verifiable snapshot (re-verify, don't trust): 9 command handlers, 7 query handlers, every
command/query validated (convention-enforced), 0 CQRS violations, full suite ~693 tests green
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

Detailed evidence and verification for the four 2026-08-04 findings lives in the
[post-IdP review](reviews/ARCHITECTURE_REVIEW-2026-08-04-post-idp.md).
Detailed evidence and verification for the five runtime findings lives in the
[runtime-hardening review](reviews/ARCHITECTURE_REVIEW-2026-08-04-runtime-hardening.md).
Detailed evidence, the seventeen Low findings, and the three dismissed candidates from the
2026-09-02 pass live in the
[whole-solution review](reviews/ARCHITECTURE_REVIEW-2026-09-02-whole-solution.md).

- **OPEN (2026-09-02) — exception-mapped responses lose every security header and the
  correlation-id echo.** Payload capture, HSTS and `UseSecurityHeaders` all write headers eagerly;
  `UseExceptionHandler` calls `Response.Clear()` before writing ProblemDetails, so every 400/404/
  409/503/500 ships without CSP, `X-Frame-Options`, `Strict-Transport-Security` or
  `X-Correlation-ID`. Register both header sets via `Response.OnStarting` (reordering does not
  help) and assert the headers on a 409 in an integration test.
- **OPEN (2026-09-02) — the payload-archive health check builds a `BlobServiceClient` and
  `DefaultAzureCredential` per probe.** Registered only via `AddCheck<T>`, so it is activated per
  run; in managed-identity mode each probe walks the credential chain and hits IMDS, reachable
  from the unpredicated `/health` and from `/healthiness`, both rate-limit-exempt. Inject the
  existing singleton client.
- **OPEN (2026-09-02) — route-path entity references bypass the sensitive-name filter.** The
  metadata and JSON-body branches screen with `IsSensitivePropertyName`; the route branch and
  caller-supplied `EntityReferences` do not, so an unauthenticated
  `GET /api/v1/nationalId/<value>` lands the value in a blob name and an Information log line,
  contradicting the extractor's own comment and `docs/DECISIONS.md`. Thread `sensitiveTokens`
  through and add a route-derived test case.
- **OPEN (2026-09-02) — four raw IL byte loops remain in convention tests.** `DomainConventionTests`,
  `CachingConventionTests`, and both AppHost.Tests IL helpers scan bytes without operand
  advancement, so a false hit skips the next four bytes; that is a silent pass for the two
  negative checks and the cache-invalidation cohort filter. Migrate to `IlInstructionWalker`
  (link the file into AppHost.Tests) and add a meta convention.
- **OPEN (2026-09-02) — `scripts/reset-servicebus-emulator.sh` fails on bash 3.2 and skips the
  network step.** `mapfile` is bash 4; macOS ships 3.2, so the script exits 127 before touching a
  container. It also omits the `docker network rm` the skill calls essential and is referenced
  from nowhere. Make it portable, add the network step, link it from the skill.

- **OPEN (2026-08-04) — CORS does not expose bearer challenges.** Scope and MFA shortfalls put
  the machine-actionable response in `WWW-Authenticate`, but `AddApiCors` never exposes that
  response header, so cross-origin browser clients cannot read the advertised scope or
  `acr_values`. Fix both CORS branches and add an Origin-based regression test.
- **OPEN (2026-08-04) — malformed OIDC authorities pass startup validation.** The application
  validates only that `Identity:Authority` is nonblank; malformed, relative, or scheme-incompatible
  values fail later in the bearer metadata path. Validate an absolute URI and its HTTPS posture at
  startup, with negative option tests.
- **OPEN (2026-08-04) — the identity convention misses raw Authorization-header reads.** The
  convention scans for `ClaimsPrincipal` and retired gateway headers but not
  `Request.Headers.Authorization`, `HeaderNames.Authorization`, or the literal. Extend the IL scan
  and prove it against an injected violation before reverting to green.
- **OPEN (2026-08-04) — converted OIDC tooling has an undeclared Python dependency.** Token parsing
  invokes `python3` before the smoke test's capability fallback, and the shared IdP helper invokes
  it while DAST/performance requirements omit it. Make parsing portable or require and preflight
  Python consistently.
- **OPEN (2026-08-04) — quoted database passwords can leak into logs.** The regex mask used by the
  migrator and Development API stops at the first semicolon, so a valid quoted password can expose
  its suffix. Parse and sanitize structurally, centralize the helper, and regression-test quoted
  values.
- **OPEN (2026-08-04) — errored outbox rows can lose their replay-retention window.** Cleanup ages
  failures from the event's `OccurredOnUtc`, not from when the row became permanently errored. Add
  `ErroredOnUtc`, retain from failure time, and keep both replay paths in sync.
- **OPEN (2026-08-04) — payload cleanup has a fixed throughput ceiling below modest traffic.** The
  hourly job deletes at most 500 blobs per prefix by default while the archive creates roughly one
  blob per request, so expired PII accumulates whenever ingestion outpaces deletion. Drain bounded
  pages and expose cleanup-backlog health.
- **OPEN (2026-08-04) — concurrent customer creation can return another request's row.** The
  natural-key recovery lookup runs on the execution strategy's first invocation as well as retries,
  allowing a same-email race to return the winner's representation as a successful create. Separate
  retry recovery from first-attempt conflict handling and add a PostgreSQL race test.
- **OPEN (2026-08-04) — Redis failures take healthy reads down.** Cache get, tombstone, and set
  exceptions escape the caching behavior, so a Redis outage can return `500` before or after a
  successful database read. Fail open on non-cancellation cache failures without weakening the
  tombstone guard.

Decisions / watch-items / explained deferrals follow.

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
