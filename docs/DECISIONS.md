# Recorded Decisions and Subsystem Notes

Long-form rationale that `CLAUDE.md` only summarises. Read the relevant section when you are about to touch that subsystem — not before.

Each recorded decision states what was chosen, what was rejected, and the **re-add trigger**: the specific falsifiable fact that would justify revisiting it. Absent that fact, the decision stands. See also [`DERIVATION-PRUNING.md`](DERIVATION-PRUNING.md), which applies the same trigger discipline to removing features from a derived project.

## Exceptions are the app-wide error model

Intentional failure signals use dedicated types keyed in `ResolveExceptionStatusCode`: `DomainRuleException` → 409, `EntityNotFoundException` → 404, `ForbiddenAccessException` → 403. Bare BCL `InvalidOperationException`/`KeyNotFoundException` are bugs and fall through to 500 — the BCL throws those itself (LINQ `.Single()`, dictionary misses), so mapping them to client-fault codes would disguise server bugs as 409/404 and hide them from 5xx alerting. `ExceptionConventionTests` blocks them from Domain and Application code via IL `newobj` inspection. Queries still signal not-found by returning null; endpoints map null to 404.

**Why not `Result<T>`/ErrorOr:**

1. The exception channel is unavoidable — `DbUpdateConcurrencyException` → 409 is already in the mapping table, and Npgsql constraint violations, cancellation, and `FeatureDisabledException` all throw. `Result<T>` would be a *second* error channel beside it, not a replacement.
2. Pipeline behaviors compose over bare `T`: `CachingBehavior` would have to decide whether to cache failures, and `OwnerAuthorizationBehavior`'s post-handler assertion relies on failed handlers throwing.
3. The always-valid entity pattern (validating public constructors) can't return Results without switching to factory methods.
4. A single closed mapping table is one mechanical rule; `Result<T>` reintroduces a per-throw-site judgment call about which failures belong in the Result.

**Re-add trigger (local only):** a component with expected, frequent, locally-handled failures — a batch import or parsing pipeline — may use `Result<T>` internally. Never as the app-wide model, and never crossing the mediator or endpoint boundary.

## The domain event is the integration/wire contract

There is deliberately no separate `IIntegrationEvent`/`ExternalEvent` type and no in-process domain-event dispatcher. The single `IDomainEvent` an aggregate raises is the same object serialized into the outbox, published to the `domain-events` topic, consumed by the Functions subscribers, and archived full-fidelity (so it may contain PII).

The usual reason to split the two — keeping an internal model from leaking into a contract consumers depend on — is handled mechanically instead: each event exposes a stable versioned `EventType` via a `const Contract` (e.g. `order.created.v1`), `OutboxMessage.Create` persists that contract rather than the CLR type name, and `EventContractSnapshotTests` renders every event through the real create path and byte-compares against fixtures in `src/StarterApp.Tests/Contracts/snapshots/`. A property rename, removal, or reorder under the same contract id fails the build with a pinned-vs-actual diff, so a class rename cannot silently break a subscriber.

The assumption this bakes in: **every domain event is a public, archived contract.** Any property added to one is also a wire-contract and a data-retention decision.

Updating a contract deliberately: `UPDATE_EVENT_SNAPSHOTS=1 dotnet test --filter EventContractSnapshot`, then choose between a compatible change, an `OutboxMessage.SchemaVersion` bump, and a new `.v2` contract. New events need a representative instance plus fixture — completeness is test-enforced.

**Re-add trigger for a translation layer:** the first time a domain event needs a property the external contract shouldn't expose (rich internal state, entity references, PII you don't want archived), **or** the first time you need an in-process, same-transaction reaction to a domain event.

## Payload capture runs first

`UsePayloadCapture()` is deliberately the first middleware, ahead of exception handling, authentication, and rate limiting. Rejected traffic (401/403/429, 404 junk) is captured *by design* as part of the full-fidelity audit posture. Request-path amplification is bounded by `MaxPayloadBytes`, `CapturedContentTypes`, and `MaxEntityReferences`; total inbound volume is the upstream gateway's problem.

The only exclusions are the six platform probe routes (`/health`, `/health/ready`, `/health/live`, `/alive`, `/liveness`, `/healthiness`) — exact-match and hardcoded, with a test pinning that the skip list can never cover the business surface. Response capture runs on an unlinked token, and still runs when the client aborts, so a deliberate disconnect can't suppress the audit record.

**Re-add trigger:** none. Moving capture behind the rate limiter requires a new recorded decision.

## AppHost projects are exempt from lock files

`StarterApp.AppHost` and `StarterApp.AppHost.Tests` set `RestorePackagesWithLockFile=false` and commit no `packages.lock.json`. The Aspire SDK injects host-RID-specific *direct* packages (`Aspire.Dashboard.Sdk.<rid>`, `Aspire.Hosting.Orchestration.<rid>`) chosen by whichever machine runs restore — `osx-arm64` from a Mac, `win-x64` from Windows, `linux-x64` in CI. No single lock file satisfies `--locked-mode` on all three, which caused recurring CI churn: every developer's restore flipped the RID and broke Linux CI.

The other six lock files are RID-agnostic and stay locked, so reproducible locked restore is preserved everywhere else.

**Re-add trigger:** none. Do not re-add lock files to these projects or switch CI to `--force-evaluate` to "fix" a recurrence — the exemption *is* the fix.

## NuGet signature validation is deliberately off

The repo-root `NuGet.config` `<clear/>`s inherited sources, declares only nuget.org, and binds every package id via `<packageSourceMapping>` — the primary dependency-confusion defense. It is COPY'd into all three Docker builds so container restores honour it.

`signatureValidationMode=require` + `<trustedSigners>` is **not** enabled: trusting only the current nuget.org repository cert breaks restore on older packages carrying a pre-rotation countersignature (e.g. `System.Security.Cryptography.ProtectedData 4.5.0` → NU3034), and enforcement differs by OS (passes macOS, fails Linux/Docker). Tamper detection is already covered by lock-file content hashes plus locked-mode restore.

**Re-add trigger:** a clean-cache Linux restore passes across the full package set with `trustedSigners` enabled.

## Reads go through Dapper, not EF Core raw SQL

Query handlers read through Dapper over a **transient** `IDbConnection` (registered in `ServiceCollectionExtensions.AddPersistence`), not through the scoped `DbContext`'s raw-SQL surface (`SqlQuery<T>` / `FromSql`). The CQRS read/write split is a wiring fact, not a naming convention:

- Each resolving handler gets its own `NpgsqlConnection`, so concurrent query handlers in one request scope can't collide — Npgsql has no MARS and `DbContext` is not thread-safe. Pooling reuses the physical sockets, so per-resolution connections are cheap.
- Reads carry no change tracker and no ambient transaction; there is nothing to accidentally mutate, save, or enlist.
- Transient-fault retry is explicit and enforced: every Dapper call is wrapped in `PostgresRetryPolicy.ExecuteAsync`, pinned by `DapperConventionTests.QueryHandlers_MustUsePostgresRetryPolicy` (IL-level, with a meta-test so the check can't go vacuously green). Writes get the same posture from EF's `EnableRetryOnFailure`.
- Parameterization is injection-safe by default: anonymous-object parameters (`new { query.CustomerId, ownerScope.OwnerSubject, ownerScope.TenantId }`) are the only idiom, on queries that carry the verified tenant/subject. There is no raw-string overload to misroute an interpolated value into.
- Dapper capabilities the EF raw-SQL surface lacks stay available: multi-mapping (`splitOn`), `QueryMultiple`, custom type handlers.

The EF alternatives were rejected on their own terms, not just by comparison. `FromSql<TEntity>` returns tracked entities and demands every mapped column — the opposite of a projected read model, and a collision with `DapperConventionTests.QueryHandlers_MustNotUseSelectStar`. `SqlQuery<T>` is closer but runs on the scoped `DbContext` (single connection, thread-affine), has no multi-mapping or multiple result sets, and splits into `SqlQueryRaw`/interpolated overloads where an interpolated string routed to the `Raw` overload is a live SQL-injection hole.

**Re-add trigger for EF-raw-SQL reads:** a cross-cutting requirement that *all* SQL flow through EF interceptors/diagnostics (query tagging, audit), or Dapper blocking a .NET/Npgsql upgrade. If the trigger fires, converge in one deliberate change that also rewrites `DapperConventionTests` — never mix the two read idioms per-handler.

## Cache refresh runs on the caller, never a background scope

Cached entries are wrapped in an envelope carrying `RefreshAfterUtc`. Inside the final `CacheRefreshWindow` of a key's TTL, exactly one request per replica recomputes **inline** via in-process single-flight while concurrent requests keep serving the cached value, so a hot key never expires under load. Unreadable or pre-envelope entries degrade to a miss and are rewritten.

The recompute deliberately runs on the caller rather than in a background scope: cache keys for owner-scoped queries include the verified tenant and subject, and a background scope has no caller identity — recomputing there would populate an owner-scoped key from the wrong (or no) identity. That is cache poisoning, not a stale read.

A failed refresh-ahead recompute logs a warning and serves the still-within-TTL cached value rather than turning a cache hit into a 500 (the RFC 5861 serve-stale-on-error shape). Cancellation rethrows; a plain miss still propagates. Invalidation is likewise best-effort — `CacheInvalidator` catches non-cancellation failures and logs, because a transient cache outage must not turn an already-committed write into a 500. The stale entry self-heals at its TTL.

**Re-add trigger for list caching:** if list queries ever need caching, use a versioned-namespace approach rather than relaxing the by-id rule — `IDistributedCache` still has no pattern deletion.

---

## Outbox and eventing

Domain events are raised inside aggregates and persisted to `outbox_messages` by `DomainEventsInterceptor` (a `SaveChangesInterceptor` wired by `AddPersistence`, pinned by `PersistenceConventionTests.AddPersistence_WiresDomainEventsInterceptor`) during a single `SaveChangesAsync`. Single-save means no user transaction, which keeps `EnableRetryOnFailure` safe for transient PostgreSQL faults.

`OutboxProcessor` claims a batch in a short transaction using `ProcessingId`/`LockedUntilUtc` plus `FOR UPDATE SKIP LOCKED`, publishes **outside** the lock, then persists outcomes in one save. A row whose claim was stolen by another replica is detached so the rest of the batch's outcomes still persist. Errored rows are skipped on later polls; `ProcessedOnUtc` strictly means published.

Recovery is the DbMigrator replay verb, not manual SQL:

```bash
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --id <guid>
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --all-errored
```

Replayed publishes carry `Replay`/`ReplayCount` application properties so audit can distinguish a republish from a first delivery. Dead-lettered subscription messages follow [`runbooks/event-replay.md`](runbooks/event-replay.md).

Retention: processed rows past `OutboxProcessor:RetentionDays` (default 30, counted from `ProcessedOnUtc`) and errored rows past it counted from `ErroredOnUtc` (the moment they became permanently errored, never the event time, so a failure after a long outage always gets a full replay window) are purged; pending and locked rows are never touched. Background work leaves a queryable trail in `job_runs` via `IJobRunRecorder` — one aggregate health row per `HealthRowIntervalMinutes` (default 15) that saw activity, never per message. Recording is a fail-open sidecar; a history write never breaks the job.

Service Bus registration is conditional on `ConnectionStrings:servicebus` — a no-op when absent, **but only in Development/Testing**. Other environments fail startup loudly so a typo'd connection string can't silently disable eventing.

Topology (topic, subscription names, filters, TTLs, dedup, delivery counts) is centralized in `ServiceBusTopology`; AppHost wires it through the fluent API from those constants. Deployed posture is a 24h TTL with `DeadLetteringOnMessageExpiration`, so events outliving a consumer outage dead-letter for replay instead of silently vanishing. Duplicate detection uses a 5-minute window keyed on `MessageId` = outbox row Guid, absorbing at-least-once republishes.

On the consuming side, Azure Functions subscribe through topic subscriptions with correlation filters (`email-notifications`, `inventory-reservation`); AppHost runs the Functions project through the Functions Docker runtime so trigger listeners are active without extra local tooling. `MessageSettlement` explicitly retries handler work with bounded exponential delays (5, 10, 20, 40, 45 seconds) and one four-minute execution deadline inside the five-minute lock-renewal window. Service Bus does not support the Functions runtime execution-retry policy; the former root `host.json` retry block was ineffective. Completion runs outside the handler retry loop so an uncertain settlement cannot repeat successful handler work in-process. Cancellation or deadline expiry leaves the message unsettled; exhausted handler retries abandon for broker redelivery.

**The emulator caveat is load-bearing:** the Service Bus emulator crash-loops on any TTL above 1 hour (exit 139), so run mode clamps every TTL through `ServiceBusTopology.ClampForEmulator` while publish mode keeps the 24h posture. Never assign the 24h constants to emulator topology directly. Further emulator gotchas are in `.claude/skills/development-workflow/SKILL.md`.

## OIDC/JWT identity (replaced the gateway-assertion model, 2026-08-01)

The API validates OIDC/JWT bearer tokens itself via `AddJwtBearer` against the configured authority (`Identity:Authority` / `Identity:Audience`). This **replaced** the previous trusted-gateway model, in which APIM verified callers and forwarded a custom HMAC-signed assertion (`X-Gateway-Assertion` + projected `X-Authenticated-*` headers). The old model is preserved in full at the `pre-idp-conversion` tag.

**Why it was replaced (the recorded trigger fired):** the architecture review's accepted risks were explicitly conditioned on a trusted perimeter, with "revisit on zero-trust networks or regulated contexts" as the trigger — and zero trust became a requirement. Three specific gaps: (1) *transitive identity* — the API verified the gateway's claims about the caller, never the caller's own credential; (2) *symmetric key* — HMAC makes every verifier a minter, so an API compromise could forge identities; (3) the replay/body-signing acceptances presumed the perimeter. An asymmetric-signature + `jti` hardening of the assertion model was considered and rejected because it leaves gap (1). Any upstream gateway is now an edge concern (WAF, rate limiting, routing), never the identity oracle.

**Mechanics:**

- Self-contained JWTs only. **No token-introspection call may sit on the request path** — the bearer handler's `ConfigurationManager` caches discovery + JWKS in memory with rate-limited refresh on unknown `kid`, so steady-state validation is a CPU-only asymmetric verify. One registered handler, one `TokenValidationParameters`.
- Claims map to `ICurrentUser` in exactly one place (the identity infrastructure in `Api/Infrastructure/Identity/`): `sub` → Subject, `tid` → TenantId, `scp`/`scope` → Scopes, `amr` → AuthenticationMethods. **`sub` and `tid` are both required** — owner scoping, cache keys, and rate-limit partitions key on subject + tenant, so a token missing either maps to no identity and the request 401s at the scope filter; an IdP without its tenant mapper fails loudly instead of stamping rows with an empty tenant. `pty` is an optional custom claim a deployer's IdP may set to `Service` to mark non-user principals (daemons, service accounts); absent or anything else means `User`. Production code never reads `HttpContext.User`, raw claims, or the `Authorization` header outside that namespace — convention-enforced. **IdP portability:** IdPs that don't natively emit `tid` or `amr` in *access* tokens (Auth0 is the canonical example; Entra emits both, the dev Keycloak realm maps them) must stamp these via their claim-customization mechanism (e.g. Auth0 Actions), and where the IdP otherwise issues opaque access tokens (again Auth0), callers must pass an explicit `audience` request parameter. The one code-change scenario: an IdP that cannot emit the bare claim name `tid` needs a namespaced-claim lookup added in `JwtIdentityMiddleware` — one line at the single mapping site. Per-IdP profile docs (`docs/idp-profiles/<idp>.md`) are written and live-verified only when a real deployment to that IdP is planned, never speculatively.
- Route metadata: every `/api/v1` group requires authorization; `RequireScope(...)` and `SecuredBy2Fa()` endpoint filters enforce scopes and MFA (`amr` must include `mfa`) from `ICurrentUser`. Health probes stay anonymous.
- Shortfall responses are machine-actionable step-up challenges (`BearerChallenges`): a scope gap 403s with `WWW-Authenticate: Bearer error="insufficient_scope", scope="<the endpoint's full required set>"` (RFC 6750), the missing-`mfa` 403 carries `error="insufficient_user_authentication"` plus `acr_values` when `Identity:StepUpAcrValues` is configured (RFC 9470), and a contract-deficient token (validated signature, missing `sub`/`tid`) 401s with `error="invalid_token"`. Elevation is always a client ↔ IdP re-authorization and retry — tokens are immutable, the API stays stateless and never proxies the step-up, and the `amr`-based access check is unchanged (`acr_values` is advisory routing for the client's next authorization request).
- Short access-token lifetimes and strict audience validation are the baseline. Tokens are plain bearer — **sender-constraining (DPoP / mTLS-bound tokens) is deliberately not implemented yet**; the corresponding replay finding is recorded in `docs/ARCHITECTURE_REVIEW.md` with its own trigger.
- Dev loop: Aspire runs a Keycloak container with an imported realm (asymmetric RS256, JWKS published), so local dev exercises the same discovery → JWKS → verify path as production. Tests use a self-issued RSA test JWT signer with an in-memory JWKS; there is no unsigned or bypass mode in any environment.

The correlation id is no longer signature-bound (nothing signs over it anymore): `PayloadCaptureMiddleware` sanitizes as before for the echoed and archived id, and lossy sanitization still appends a raw-bound hash suffix so distinct raw ids never collapse into one archive stream.

Owner-scoped resources (Customer, Product, Order) go further than authentication: create handlers stamp `OwnerSubject`/`TenantId` from `ICurrentUser`, query handlers filter by owner scope, and mutation handlers call `IOwnerOnlyPolicy` before touching a loaded aggregate. Cross-owner reads are hidden as not-found or empty lists; cross-owner mutations return 403. Policy invocation is verified structurally, not just by convention: non-create commands implement `IOwnerAuthorizedMutation`, `OwnerOnlyPolicy.Authorize` records its evaluation on a scoped tracker, and `OwnerAuthorizationBehavior` asserts afterwards that the policy actually ran — throwing in Development/Testing so the suite catches inject-but-never-call, and logging an error in production (the mutation is already persisted; failing the response wouldn't undo it).

Rate limiting partitions by verified tenant/subject for protected endpoints and falls back to IP only for public requests. The k6 perf gate lifts `PermitLimit` because its entire load runs under one identity.

**Re-add trigger for a gateway-verified model:** none — reintroducing perimeter-anchored identity requires a new recorded decision reversing the zero-trust requirement itself.

**Re-add trigger for a local gateway hop (the deleted `StarterApp.Gateway`):** a deployed gateway-interaction bug class that needs local reproduction (forwarded headers, scheme-dependent redirects, auth-header handling across the hop, streaming/buffering or timeout behavior), or production APIM policies starting to mutate requests in ways the API must tolerate. When the trigger fires, prefer the APIM self-hosted gateway container (real policy execution) over a YARP stand-in — YARP reproduces a generic reverse proxy, not APIM behavior. Either way the hop stays a passthrough: identity remains validated in the API, never re-anchored at the proxy.

## Payload archive and PII audit

Every inbound and outbound payload is captured through the shared capture service: HTTP request/response bodies, outbound Service Bus messages, inbound Function messages, and generated artifacts via `IArtifactCaptureSink`.

- **Archive** blobs are correlation-bound JSONL under `archive/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl` — all operations for one correlation id in that minute append to the same file.
- **Audit** blobs are time-window JSONL under `audit/{yyyy-MM-dd}/{HH}/{mm}/payload-audit.jsonl`. HTTP audit rows carry a business-action taxonomy (`action`: Create/Read/Update/Delete/StatusChange — verb-derived on request rows, override-aware via `WithAuditAction(...)` on response rows) plus the verified subject/tenant, so support can answer "all deletes by subject X" from audit rows alone.
- **Entity index** blobs under `entity-index/{entityType}/{entityId}/…` are pointer-only. They must not duplicate the payload. Entity-reference extraction consults `SensitivePropertyNames` and requires a real `Id`/`_id` suffix, so a sensitive `*Id` (e.g. `nationalId`) never becomes a blob path segment.

Append-blob writes are atomic per record: a JSONL line exceeding one 4 MiB append block goes to a single-writer `<blobName>.oversize-<id>.jsonl` sidecar (same minute path, so retention covers it) and the shared stream gets a pointer line. Multi-block appends into a shared blob could interleave with concurrent writers and splice records.

Archive and audit are full-fidelity and may contain PII; **logs must stay redacted** — use the shared JSON redactor plus `Serilog.Enrichers.Sensitive`, and never log raw `{Body}` values. Capture logs must include the archive, audit, and entity-index blob names so support can jump from a log line to the artifact.

Failure policy is **per channel**, because an audit sidecar must not take down synchronous user traffic. `PayloadCapture:HttpFailureMode` and `PayloadCapture:ServiceBusFailureMode` both default to `FailOpen` in code so standalone dev and tests with no archive store never break. Production-like orchestrations set `RequireArchiveStore=true` and `ServiceBusFailureMode=FailClosed`; HTTP stays `FailOpen` unless a compliance domain opts in. Under `FailClosed`, `OutboxProcessor` treats a capture failure as **pause-the-batch** rather than poisoning the message — so no event publishes without a durable audit record, and none is permanently lost.

`PayloadArchiveCleanupFunction` is timer-triggered from `PayloadCapture:CleanupCron`, supplied via the `PayloadCapture__CleanupCron` environment variable. The trigger's `%…%` lookup must use the `:` config-key form because the env provider normalizes `__`; a convention test enforces this. The Functions image bakes an hourly default so a missing setting can't fail function indexing and take down the Service Bus subscribers in the same worker.

## Perf and security gates

- **k6** (`tests/k6/`): `smoke.js` and `load.js` run against an Aspire-started API. `.github/workflows/perf.yml` runs nightly plus on dispatch via `run-perf.sh`, which boots a throwaway PostgreSQL, migrates, bulk-seeds 20k owner-scoped rows so list and index paths run at realistic volume, provisions a throwaway Redis so by-id reads measure a prod-like round trip, and fails on any threshold breach. List checks enforce a volume floor so a fast-but-empty response can't pass. Details in `tests/k6/README.md`.
- **DAST** (`dast/run-dast.sh`): OWASP ZAP against a seeded throwaway stack, failing at or above `FAIL_RISK`. The gate also fails on a dead scan — a non-clean ZAP exit is not swallowed, and a URL-discovery floor rejects a green-but-reached-nothing report. A scripted cross-owner probe afterwards catches the IDOR class a single-identity scan is blind to. False positives are suppressed by narrow scoped `alertFilter` entries, never by widening exclusions. Details in `dast/README.md`.

## `AnalysisMode=All` with a curated `.editorconfig` severity policy

`Directory.Build.props` enables every .NET analyzer. A strict first build (2026-05-24) failed with 45 errors and a full inventory produced 1,334 diagnostics across 41 rule ids, so the mode is only workable with an explicit severity policy, which lives in `.editorconfig` under the `AnalysisMode=All policy` heading with a one-line reason per rule. The shape: high-signal correctness and clarity rules stay at error; rules that conflict with this template's conventions are lowered or disabled globally (`ConfigureAwait(false)` blanket use, public-to-internal churn, marker interfaces, DTO collection shapes, localization, the `next` delegate name); test-only noise (underscore test names, disposal of harness lifetimes, reflection-discovered helpers) is suppressed for test projects only.

**Why not fix everything or pick a preset:** the mechanical fixes would rewrite Minimal API, CQRS, DTO, and xUnit code into shapes the convention tests forbid, and a lower preset would drop the security and correctness rules that are cheap to keep.

**Re-add triggers:** revisit CA1062 only as a focused null-guard hardening task; CA1848 and CA1873 only if logging allocation becomes a goal (source-generated logging touches API, Functions, and shared code); CA1031 case by case during reliability work, never as a blanket cleanup.

## Layered monolith, not a modular monolith (design on file, not adopted)

Code is partitioned by technical concern (`Domain/Entities`, `Application/Commands`, one `ApplicationDbContext`), not by business capability, and `CreateOrderCommandHandler` reaches across Customers, Catalog, and Orders in one transaction: it reads the customer, runs the atomic `UPDATE products SET stock = stock - qty WHERE stock >= qty` anti-oversell guard, and inserts the order. At three aggregates this is the right weight.

If the domain grows, the recorded target is **synchronous modules behind published contracts**, not an asynchronous saga. Modules (`Customers`, `Catalog` owning stock, `Orders`) live under `Modules/<Name>/{Contracts,Domain,Application,Data}`; other modules may reference only `Contracts/` (small read interfaces such as `ICatalogReads`, `ICustomerReads`, and one operation interface `IInventory.ReserveStockAsync`, all carrying the owner scope). Start folder-only and graduate to one project per module so `internal` enforces the boundary. Cross-module reads use denormalised snapshots (already how `order_items` carries product name and price) or in-memory composition, never a cross-module JOIN or foreign key. Convention tests pin it: no handler references another module's `Domain` or `Data`, no cross-module `DbSet` access, no cross-schema foreign key, `Contracts/` holds only interfaces and records.

**The one deliberate compromise:** `IInventory` enlists in Orders' ambient transaction so the atomic stock guard keeps overselling impossible. A strict modular monolith forbids that; it is accepted for this invariant alone and would be pinned by a convention test.

**Why not the saga (Approach B):** order creation would become `Pending` → `StockReserved` or `StockReservationFailed` → confirm or compensate through the existing outbox and `inventory-reservation` subscription. That trades prevention of overselling for detection plus compensation and adds process-manager state for an invariant that does not warrant it. Reserve the saga for steps that genuinely tolerate eventual consistency, such as the already-async email notification.

**Re-add trigger:** the same as the folder-only Clean Architecture acceptance in `ARCHITECTURE_REVIEW.md`: the domain grows past the sample aggregates, a second team owns part of it, or a compiler-enforced boundary is required.

## Considered and rejected

Recorded so future sessions do not re-propose them.

- **MediatR, AutoMapper, the repository pattern, an in-process background task queue.** Each conflicts with a documented prohibition or an existing mechanism (custom mediator, explicit mappers, DbContext directly, transactional outbox for anything that must survive a restart).
- **Production infrastructure as code (Bicep or azd).** Maintainer decision, 2026-06-10: deployment topology is owned by the hosting environment; Aspire is the only orchestration path in this repo.
- **WORM immutability on audit blobs as a roadmap item.** Cannot be expressed here (the emulator does not enforce it and there is no IaC). Recorded instead as an accepted limitation with deployer guidance in `ARCHITECTURE_REVIEW.md`.
- **A `spikes/` folder convention.** Maintainer decision, 2026-06-10: experiments go through normal branches and worktrees.
- **Client-IP extraction chains in middleware.** The API runs behind a trusted edge; the edge owns client network identity.
- **List-query caching.** No pattern-based invalidation in `IDistributedCache`; revisit only with a versioned-namespace design (see the caching decision above).
- **TypeScript client codegen.** Marginal for an API-only template; the OpenAPI output already serves contract consumers.
- **A request-row audit `action` stamp.** Captured before routing, the verb-derived value was wrong on exactly the override routes and duplicated `method`; the response row is the authoritative carrier (complexity review, 2026-06-12).
