# Recorded Decisions and Subsystem Notes

Long-form rationale that `AGENTS.md` only summarises. Read the relevant section when you are about to touch that subsystem — not before.

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

The usual reason to split the two — keeping an internal model from leaking into a contract consumers depend on — is handled mechanically instead: each event exposes a stable versioned `EventType` via a `const Contract` (e.g. `order.created.v1`), `OutboxMessage.Create` persists that contract rather than the CLR type name, and `EventContractSnapshotTests` renders every event through the real create path and byte-compares against fixtures in `tests/StarterApp.Tests/Contracts/snapshots/`. A property rename, removal, or reorder under the same contract id fails the build with a pinned-vs-actual diff, so a class rename cannot silently break a subscriber.

The assumption this bakes in: **every domain event is a public, archived contract.** Any property added to one is also a wire-contract and a data-retention decision.

Updating a contract deliberately: `UPDATE_EVENT_SNAPSHOTS=1 dotnet test --filter EventContractSnapshot`, then choose between a compatible change, an `OutboxMessage.SchemaVersion` bump, and a new `.v2` contract. New events need a representative instance plus fixture — completeness is test-enforced.

**Re-add trigger for a translation layer:** the first time a domain event needs a property the external contract shouldn't expose (rich internal state, entity references, PII you don't want archived), **or** the first time you need an in-process, same-transaction reaction to a domain event.

## Payload capture runs first

`UsePayloadCapture()` is deliberately the first middleware, ahead of exception handling, authentication, and rate limiting. Rejected traffic (401/403/429, 404 junk) is captured *by design* as part of the full-fidelity audit posture. Request-path amplification is bounded by `MaxPayloadBytes`, `CapturedContentTypes`, and `MaxEntityReferences`; total inbound volume is the upstream gateway's problem. An HTTP capture's *entity-index* lines are the one deferred write (2026-09-27; every HTTP capture since 2026-10-03): the archive and audit record are written first as always and list the entity references, but the `entity-index/...` lines of the request and of the response, whatever their content type, are appended after the pipeline and only for an authenticated caller, so neither an anonymous body nor an anonymous `GET /api/v1/customers/424242` answering 401 can plant ids beside real ones in the index an investigator searches by.

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

## Feature toggles fail closed (2026-10-05)

A `[FeatureToggle]` is on only when `FeatureToggles:{name}` says `true`; a missing entry is off, and the API refuses to start when configuration under `FeatureToggles` names a key no `[FeatureToggle]` declares (compared case-insensitively, as configuration keys are), naming the stray key and the known ones, and when an entry is anything but `true` or `false` (`DeclaredFeatureToggles`, validated on start; before 2026-10-10 a value such as `off` passed start-up and threw on every request). Removing a `[FeatureToggle]` while a deployed override still names it refuses start-up too, so the override goes first and the attribute in the next deploy. Before, a missing entry meant on, so a misspelt override in an app setting or environment variable was a stray key nobody read and the feature it meant to switch off stayed on. Rejected: keeping on-by-default and relying on the convention test, which checks `appsettings.json` but cannot see the deployed configuration. **Re-add trigger:** a toggle whose feature must keep running when its configuration is lost, at which point that toggle carries its own default rather than the rule changing for all.

---

## Outbox and eventing

Domain events are raised inside aggregates and persisted to `outbox_messages` by `DomainEventsInterceptor` (a `SaveChangesInterceptor` wired by `AddPersistence`, pinned by `PersistenceConventionTests.AddPersistence_WiresDomainEventsInterceptor`) during a single `SaveChangesAsync`. Single-save means no user transaction, which keeps `EnableRetryOnFailure` safe for transient PostgreSQL faults.

`OutboxProcessor` claims a batch in a short transaction using `ProcessingId`/`LockedUntilUtc` plus `FOR UPDATE SKIP LOCKED`, publishes **outside** the lock, then persists outcomes in one save. A row whose claim was stolen by another replica is detached so the rest of the batch's outcomes still persist. Errored rows are skipped on later polls; `ProcessedOnUtc` strictly means published. A full batch that published cleanly goes straight to the next claim instead of sleeping `PollingIntervalSeconds`, so a backlog drains at broker speed; a batch with any retry or pause waits the interval, since retried rows are reclaimable at once.

Recovery is the DbMigrator replay verb, not manual SQL:

```bash
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --id <guid>
dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --all-errored
```

Replayed publishes carry `Replay`/`ReplayCount` application properties so audit can distinguish a republish from a first delivery. Dead-lettered subscription messages follow [`runbooks/event-replay.md`](runbooks/event-replay.md).

Retention: processed rows past `OutboxProcessor:RetentionDays` (default 30, counted from `ProcessedOnUtc`) and errored rows past it counted from `ErroredOnUtc` (the moment they became permanently errored, never the event time, so a failure after a long outage always gets a full replay window) are purged; pending and locked rows are never touched. Background work leaves a queryable trail in `job_runs` via `IJobRunRecorder` — one aggregate health row per `HealthRowIntervalMinutes` (default 15) that saw activity, never per message. Recording is a fail-open sidecar; a history write never breaks the job.

Service Bus registration is conditional on `ConnectionStrings:servicebus` — a no-op when absent, **but only in Development/Testing**. Other environments fail startup loudly so a typo'd connection string can't silently disable eventing.

Topology (topic, subscription names, filters, TTLs, dedup, delivery counts) is centralized in `ServiceBusTopology`; AppHost wires it through the fluent API from those constants. Deployed posture is a 24h TTL with `DeadLetteringOnMessageExpiration`, so events outliving a consumer outage dead-letter for replay instead of silently vanishing. Duplicate detection uses a 5-minute window keyed on `MessageId` = outbox row Guid, absorbing at-least-once republishes.

On the consuming side, Azure Functions subscribe through topic subscriptions with correlation filters (`email-notifications`, `inventory-reservation`); AppHost runs the Functions project through the Functions Docker runtime so trigger listeners are active without extra local tooling. `MessageSettlement` explicitly retries handler work with bounded exponential delays (5, 10, 20, 40, 45 seconds) under a 210-second deadline that covers handler execution and backoff; settlement then runs on the host token inside a 30-second reserve, all within the five-minute lock-renewal window (`FunctionsHostConfigConventionTests` and `MessageSettlementTests` pin both budgets). Service Bus does not support the Functions runtime execution-retry policy; the former root `host.json` retry block was ineffective. Completion runs outside the handler retry loop so an uncertain settlement cannot repeat successful handler work in-process. Host cancellation, or a deadline that expires mid-handler, leaves the message unsettled; a poison failure or a completed handler that surfaces after the deadline is still dead-lettered or completed; exhausted handler retries abandon for broker redelivery. The throughput consequence is deliberate: each of the 16 concurrent invocations can hold its slot for the whole deadline during an outage, so a sustained FailClosed archive outage stalls the subscription for its duration and stretches time-to-dead-letter to roughly five deliveries times the deadline plus lock lapse, instead of burning `MaxDeliveryCount` in seconds.

**The emulator caveat is load-bearing:** the Service Bus emulator crash-loops on any TTL above 1 hour (exit 139), so run mode clamps every TTL through `ServiceBusTopology.ClampForEmulator` while publish mode keeps the 24h posture. Never assign the 24h constants to emulator topology directly. Further emulator gotchas are in `.claude/skills/development-workflow/SKILL.md`.

**Subscribers process through an inbox (2026-09-27).** Delivery is at least once and unordered, and duplicate detection only covers publishes within its 5-minute window, so a subscriber that completes its work but loses the settlement sees the message again. Every `[ServiceBusTrigger]` function runs its work through `IMessageInbox.ProcessOnceAsync(consumer, MessageId, work)` (convention-tested in `PayloadCaptureConventionTests`): one transaction inserts `(consumer, message_id)` into `inbox_messages` and runs the work, so a redelivery after commit is skipped, one racing an uncommitted attempt waits on the key, and a failed attempt leaves no claim. Database side effects written through the `InboxTransaction` commit exactly once with the claim; an external side effect (the confirmation email) is at least once, because a send that succeeds before a failed commit repeats. Capture runs before the inbox, so the archive still records every delivery. Rows purge after `Inbox:RetentionDays` (7), well past the 24-hour subscription TTL after which nothing can be redelivered. Without a `database` connection string the inbox is a pass-through.

## Order creation takes an `Idempotency-Key` (2026-09-27)

The handler already survives its own retries (one stable order Id across execution-strategy attempts), but a client that retries `POST /api/v1/orders` after a timeout used to create a second order and reserve stock twice. The optional `Idempotency-Key` header closes that: `idempotency_records` keeps one row per (tenant, subject, operation, key) with a SHA-256 of the canonical request and the order Id, written in the same `SaveChanges` as the order. A repeat with a matching hash replays the stored order; a different request under the same key is a 422 (`IdempotencyKeyReusedException`); two concurrent requests collide on `pk_idempotency_records`, the loser's transaction rolls back its stock reservation, and it answers with the winner's order.

A separate table rather than a column on `orders`, so the next keyed create (Product create's open retry gap) reuses it without touching its aggregate. Rows are never purged: there is at most one per keyed order, so the table grows no faster than `orders` does. The key is optional so existing clients are unchanged.

**Re-add trigger for expiry:** keys become per-request rather than per-order (a keyed endpoint that does not create a durable resource), which would make the table outgrow what it indexes.

## OIDC/JWT identity (replaced the gateway-assertion model, 2026-08-01)

The API validates OIDC/JWT bearer tokens itself via `AddJwtBearer` against the configured authority (`Identity:Authority` / `Identity:Audience`). This **replaced** the previous trusted-gateway model, in which APIM verified callers and forwarded a custom HMAC-signed assertion (`X-Gateway-Assertion` + projected `X-Authenticated-*` headers). The old model is preserved in full at the `pre-idp-conversion` tag.

**Why it was replaced (the recorded trigger fired):** the architecture review's accepted risks were explicitly conditioned on a trusted perimeter, with "revisit on zero-trust networks or regulated contexts" as the trigger — and zero trust became a requirement. Three specific gaps: (1) *transitive identity* — the API verified the gateway's claims about the caller, never the caller's own credential; (2) *symmetric key* — HMAC makes every verifier a minter, so an API compromise could forge identities; (3) the replay/body-signing acceptances presumed the perimeter. An asymmetric-signature + `jti` hardening of the assertion model was considered and rejected because it leaves gap (1). Any upstream gateway is now an edge concern (WAF, rate limiting, routing), never the identity oracle.

**Mechanics:**

- Self-contained JWTs only. **No token-introspection call may sit on the request path** — the bearer handler's `ConfigurationManager` caches discovery + JWKS in memory with rate-limited refresh on unknown `kid`, so steady-state validation is a CPU-only asymmetric verify. One registered handler, one `TokenValidationParameters`.
- Claims map to `ICurrentUser` in exactly one place (the identity infrastructure in `Api/Infrastructure/Identity/`): `sub` → Subject, `tid` → TenantId, `scp`/`scope` → Scopes, `amr` → AuthenticationMethods. **`sub` and `tid` are both required** — owner scoping, cache keys, and rate-limit partitions key on subject + tenant, so a token missing either maps to no identity and the request 401s at the scope filter; an IdP without its tenant mapper fails loudly instead of stamping rows with an empty tenant. `pty` is an optional custom claim a deployer's IdP may set to `Service` to mark non-user principals (daemons, service accounts); absent or anything else means `User`. Production code never reads `HttpContext.User`, raw claims, or the `Authorization` header outside that namespace — convention-enforced. **IdP portability:** IdPs that don't natively emit `tid` or `amr` in *access* tokens (Auth0 is the canonical example; Entra emits both, the dev Keycloak realm maps them) must stamp these via their claim-customization mechanism (e.g. Auth0 Actions), and where the IdP otherwise issues opaque access tokens (again Auth0), callers must pass an explicit `audience` request parameter. The one code-change scenario: an IdP that cannot emit the bare claim name `tid` needs a namespaced-claim lookup added in `JwtIdentityMiddleware` — one line at the single mapping site. Per-IdP profile docs (`docs/idp-profiles/<idp>.md`) are written and live-verified only when a real deployment to that IdP is planned, never speculatively.
- Route metadata: every `/api/v1` group requires authorization; `RequireScope(...)` and `SecuredBy2Fa()` endpoint filters enforce scopes and MFA (`amr` must include `mfa`) from `ICurrentUser`. Health probes stay anonymous. The deep probes (`/health`, `/healthiness`) touch every dependency on each hit and are anonymous and exempt from rate limiting by design: they are meant to sit behind the upstream gateway, which routes only the platform's own probe traffic to them and keeps them off the public surface, the same gateway that owns inbound volume ("Payload capture runs first"). A deployment without that gateway must restrict them at its ingress.
- Shortfall responses are machine-actionable step-up challenges (`BearerChallenges`): a scope gap 403s with `WWW-Authenticate: Bearer error="insufficient_scope", scope="<the endpoint's full required set>"` (RFC 6750), the missing-`mfa` 403 carries `error="insufficient_user_authentication"` plus `acr_values` when `Identity:StepUpAcrValues` is configured (RFC 9470), and a contract-deficient token (validated signature, missing `sub`/`tid`) 401s with `error="invalid_token"`. Elevation is always a client ↔ IdP re-authorization and retry — tokens are immutable, the API stays stateless and never proxies the step-up, and the `amr`-based access check is unchanged (`acr_values` is advisory routing for the client's next authorization request).
- Short access-token lifetimes and strict audience validation are the baseline. Tokens are plain bearer — **sender-constraining (DPoP / mTLS-bound tokens) is deliberately not implemented yet**; the corresponding replay finding is recorded in `docs/ARCHITECTURE_REVIEW.md` with its own trigger.
- Dev loop: Aspire runs a Keycloak container with an imported realm (asymmetric RS256, JWKS published), so local dev exercises the same discovery → JWKS → verify path as production. Tests use a self-issued RSA test JWT signer with an in-memory JWKS; there is no unsigned or bypass mode in any environment.

The correlation id is no longer signature-bound (nothing signs over it anymore): `PayloadCaptureMiddleware` sanitizes as before for the echoed and archived id, and lossy sanitization still appends a raw-bound hash suffix so distinct raw ids never collapse into one archive stream.

Owner-scoped resources (Customer, Product, Order) go further than authentication: create handlers stamp `OwnerSubject`/`TenantId` from `ICurrentUser`, query handlers filter by owner scope, and mutation handlers call `IOwnerOnlyPolicy` before touching a loaded aggregate. Cross-owner reads are hidden as not-found or empty lists; cross-owner mutations return 403. Policy invocation is enforced at the write, not just by convention: every command except the two that only create a new aggregate implements `IOwnerAuthorizedMutation`; `OwnerAuthorizationBehavior` flags such a request on a scoped tracker before the handler runs; `OwnerOnlyPolicy.Authorize` marks the tracker; and `OwnerAuthorizationWriteGuard`, an EF interceptor, throws before `SaveChanges` or any `ExecuteUpdate`/`ExecuteDelete` if the flag is set and the mark is missing. A handler that forgets the check fails before anything is persisted, in every environment (until 2026-09-12 the behavior only checked after the handler and, in production, only logged). An authorization fallback policy requires an authenticated user on every endpoint; only the probe endpoints in `ProbeEndpoints` say `AllowAnonymous`, and `ApiConventionTests` keeps that list closed.

Rate limiting partitions by verified tenant/subject for protected endpoints and falls back to IP only for public requests. The k6 perf gate lifts `PermitLimit` because its entire load runs under one identity.

**Re-add trigger for a gateway-verified model:** none — reintroducing perimeter-anchored identity requires a new recorded decision reversing the zero-trust requirement itself.

**Re-add trigger for a local gateway hop (the deleted `StarterApp.Gateway`):** a deployed gateway-interaction bug class that needs local reproduction (forwarded headers, scheme-dependent redirects, auth-header handling across the hop, streaming/buffering or timeout behavior), or production APIM policies starting to mutate requests in ways the API must tolerate. When the trigger fires, prefer the APIM self-hosted gateway container (real policy execution) over a YARP stand-in — YARP reproduces a generic reverse proxy, not APIM behavior. Either way the hop stays a passthrough: identity remains validated in the API, never re-anchored at the proxy.

## Payload archive and PII audit

Every inbound and outbound payload is captured through the shared capture service: HTTP request/response bodies, outbound Service Bus messages, inbound Function messages, and generated artifacts via `IArtifactCaptureSink`.

- **Archive** blobs are correlation-bound JSONL under `archive/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl` — all operations for one correlation id in that minute append to the same file.
- **Audit** blobs are time-window JSONL under `audit/{yyyy-MM-dd}/{HH}/{mm}/payload-audit.jsonl`. HTTP audit rows carry a business-action taxonomy (`action`: Create/Read/Update/Delete/StatusChange — verb-derived on request rows, override-aware via `WithAuditAction(...)` on response rows) plus the verified subject/tenant, so support can answer "all deletes by subject X" from audit rows alone.
- **Entity index** blobs under `entity-index/{entityType}/{entityId}/…` are pointer-only. They must not duplicate the payload. Entity-reference extraction consults `SensitivePropertyNames` and requires a real `Id`/`_id` suffix, so a sensitive `*Id` (e.g. `nationalId`) never becomes a blob path segment.

Append-blob writes are atomic per record: a JSONL line exceeding one 4 MiB append block goes to a single-writer `<blobName>.oversize-<id>.jsonl` sidecar (same minute path, so retention covers it) and the shared stream gets a pointer line. Multi-block appends into a shared blob could interleave with concurrent writers and splice records.

Archive and audit are full-fidelity and may contain PII; **logs must stay redacted** — use the shared JSON redactor plus `Serilog.Enrichers.Sensitive`, and never log raw `{Body}` values. The redactor screens JSON field by field; a body it cannot screen never reaches a log: invalid JSON is suppressed, and free text (`text/plain`, form bodies, anything that is not JSON) is logged only as its media type, byte count and SHA-256 (2026-09-27), because it has no property names to find a password or token by. The archive keeps every body in full, so the hash links the log line to it. Capture logs must include the archive, audit, and entity-index blob names so support can jump from a log line to the artifact.

Failure policy is **per channel**, because an audit sidecar must not take down synchronous user traffic. `PayloadCapture:HttpFailureMode` and `PayloadCapture:ServiceBusFailureMode` both default to `FailOpen` in code so standalone dev and tests with no archive store never break. Production-like orchestrations set `RequireArchiveStore=true` and `ServiceBusFailureMode=FailClosed`; HTTP stays `FailOpen` unless a compliance domain opts in. Under `FailClosed`, `OutboxProcessor` treats a store's capture failure as **pause, never poison** — so no event publishes without a durable audit record, and none is permanently lost. A store failure (any storage error, or a transient one) pauses the whole batch, at Error when it is not transient (a missing role assignment stops all publishing and must be loud). A failure specific to one message holds back only that message, and the rest of the batch publishes — so one bad message cannot block everything behind it. That failure spends the message's retry budget like a failed publish (2026-10-04, from Gumnut): a capture that fails the same way every time was reclaimed every lock period for ever with nothing but a log line, and now reaches Error after `MaxRetries`, unsent, where the replay surface shows it (`docs/runbooks/event-replay.md`). Subscribers already tolerate the reordering.

**Only the archive record is written before the response (2026-09-28, from Gumnut).** A capture appends its archive line inline, one round trip. The audit line always, and the entity-index lines on a `FailOpen` channel, go to `PayloadCaptureBackgroundQueue` and are written by `PayloadCaptureBackgroundWriter`, so a request whose inbound and outbound bodies each carry a dozen ids costs two blob calls before it returns instead of twenty-eight. The audit stream was already best-effort under every policy, and a `FailOpen` index write only ever logged its failure, so deferring them changes no promise. A `FailClosed` channel keeps its index inline, because there the capture fails if the index cannot be written. The queue is bounded by `PayloadCapture:BackgroundQueueMaxBytes` (32 MiB by default, in bytes because an audit line carries the whole payload); when it is full, or after shutdown, a write is dropped, logged, and counted on the `starterapp.payload_capture.background_writes_dropped` metric. The writer drains after every other hosted service has stopped, so the last requests' lines land, and gives up only when the host's shutdown timeout runs out (also counted as drops). A failed background write is logged and not retried; the archive record it points at is already durable. Each write is given `PayloadCapture:BackgroundWriteTimeoutSeconds` (30) and is abandoned and counted as a drop when it runs out (2026-10-03), so one hung append cannot hold the single reader while the queue fills and every later line is dropped. **Re-add trigger** for writing them inline again: an audit or index line that must survive a process crash, which would make it a durable record and move it to the archive's failure policy rather than back onto the request.

`PayloadArchiveCleanupFunction` is timer-triggered from `PayloadCapture:CleanupCron`, supplied via the `PayloadCapture__CleanupCron` environment variable. The trigger's `%…%` lookup must use the `:` config-key form because the env provider normalizes `__`; a convention test enforces this. The Functions image bakes an hourly default so a missing setting can't fail function indexing and take down the Service Bus subscribers in the same worker.

## Deleting a person's record does not rewrite the payload archive (2026-09-28, from Gumnut)

Deleting a customer (`DeleteCustomerCommand`) removes the row. It does not reach the payload archive: the archive, audit and entity-index blobs captured while the record was in use keep their copies of it, findable through `entity-index/customer/{id}/`, until `PayloadArchiveCleanupFunction` deletes them at `PayloadCapture:RetentionDays` (30 by default). Blob soft delete, where the hosting environment enables it, and the database's backups hold copies for their own windows on top. `IPayloadArchiveStore` has no targeted delete, and none is built.

This is a position, not an oversight. The archive is the audit trail of what was sent and received, kept for a short fixed window for security, support and breach scoping, access controlled, never read to serve the record, and destroyed on schedule; a data-protection duty to destroy personal information is met by that schedule rather than by rewriting an append-only trail. A targeted purge would mean listing and rewriting shared append blobs (the per-minute audit stream and a correlation's archive mix several records), which breaks the property that makes the trail worth keeping. A restore to a point before a deletion brings the record back; a derived project with a legal erasure duty journals each erasure outside the database and re-applies the journal after a restore (Gumnut's erasure journal, `c81c19c`, `f1e6b20`, `aeff6ae`).

**Re-add trigger** for a targeted archive purge: an erasure request that asks about the archive specifically and will not accept the window above, a regulator's guidance that an audit window is not a sufficient basis, or a proposal to lengthen `PayloadCapture:RetentionDays` (the window is what makes this position defensible, so lengthening it reopens the decision).

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

## Dapper reads retry through `PostgresRetryPolicy`, not Polly

Read-side transient faults are retried by a hand-rolled helper (`Infrastructure/Persistence/PostgresRetryPolicy.cs`):
five attempts, full-jitter exponential backoff, and a **total delay budget of ten seconds** so a
saturated reader never holds a request open for the whole backoff ladder. Polly v8 is already in
the dependency graph via `Microsoft.Extensions.Http.Resilience`, and the 2026-09-08
simplification audit proposed swapping to it.

**Why not Polly:** its retry strategy has per-attempt delay and a max-delay cap but no total
budget; reproducing the budget means wrapping the pipeline in a timeout, which cancels the
in-flight query rather than declining the next retry. That is a different behaviour, not the same
one written shorter. Ten tests pin the current semantics, and `DapperConventionTests` requires
every Dapper handler to route through the helper.

**Re-add trigger:** Polly ships a total-delay budget for `RetryStrategyOptions`, or the read path
needs a second resilience concern (circuit breaker, hedging) that would otherwise be hand-rolled
beside the retry. Converge in one change that also rewrites `PostgresRetryPolicyTests` and the
convention's type-name pin.

## The consistency suite is advisory and human-read

`tests/StarterApp.Tests/Consistency/` scores command handlers, query handlers and EF configurations
against pinned exemplars (z-scored Mahalanobis distance with Ledoit-Wolf shrinkage, plus a
per-feature divergence list) and writes the result to `docs/_local/consistency-*.txt` on every
test run. Design rationale: <https://z3d.github.io/blog/consistency-checks/>. Its consumer is a
reviewer reading the file, so it has no code consumer by design; "nothing references its output"
is not grounds to delete it. Exemplars and their justifications live in `docs/exemplars/`.

**Why not delete it as dead tooling:** convention tests encode rules already decided; this
surfaces candidate rules by ranking files that differ in shape from the exemplars. Once review
decides a divergence is a real rule it leaves the report and becomes a convention test.

**Why not gate the build on a distance:** distance punishes the first good example of a new
pattern and rewards the tenth copy of a bad one. Two tests that pinned `CreateOrderCommandHandler`
as an outlier were removed on 2026-09-08 for exactly this reason.

**Known limit:** at template scale (5-9 members, 3-4 exemplars per cohort) the signal is thin.
It grows with the derived project.

**Remove trigger:** two consecutive dated reviews record that no report line informed a finding.

## C# 14 adoption is targeted, not a sweep (2026-09-19)

The SDK pinned in `global.json` (10.0.300) compiles C# 14 by default; C# 15 needs a .NET 11 preview SDK and `allowPrerelease` is off, so it is out of scope until .NET 11 ships (November 2026). Taken where the language feature removes real code: an `extension` block with extension properties for the parameterless `DbUpdateException` checks (`IsNotNullViolation`, `IsStringTruncationViolation`), implicitly typed lambdas where the parameter type was only there to satisfy an older compiler (IDE0350), `System.Threading.Lock` for the in-memory archive gate (IDE0330, C# 13), and auto-properties where a backing field only forwarded (IDE0032). `field`-backed properties, null-conditional assignment and `nameof` on unbound generics have no sites in this codebase today; use them when one appears.

Not applied on purpose: primary constructors (IDE0290, ~94 classes) and collection expressions for fluent chains (IDE0305, ~35 sites). Both are C# 12 and both are style-only rewrites of working code; the handlers' explicit constructors are also what the consistency cohort and the exemplar READMEs count. Re-add trigger: a deliberate decision to change the handler shape, made once here and mirrored in the forks.

Gotcha: CA1034 ("do not nest type") fires on a C# 14 extension block; `.editorconfig` silences it for that one file rather than losing the feature.

## The Entra token is the database credential; a password is the exception (2026-09-20)

`DatabaseAuthentication` (ServiceDefaults, linked into the migrator) is the one place a database credential
is decided: a connection string that carries no password means "connect as the hosting identity", and Azure
Database for PostgreSQL accepts the identity's Entra access token as the password. The user is the one the
string names or, when it names none — the shape Aspire's `AddAzurePostgresFlexibleServer` emits in Entra mode
(`Host=…;Database=…`) — the principal the token was issued to (`upn`, `preferred_username`, or the managed
identity's name from `xms_mirid`), the same derivation Aspire's own Npgsql integration performs. TLS is
forced to `Require` on that path so a refused token is never retried in the clear (Npgsql's default `Prefer`
would). Found the hard way on the first deployment, 2026-09-20: the original rule demanded a named user, so the
deployed migrator fell through to the password path and connected as the container's OS user. Every
long-running process takes its connections from the single `NpgsqlDataSource` that `AddDatabaseDataSource`
registers (`TryAddSingleton`, so the API's `AddPersistence` and ServiceDefaults' `AddJobRunRecording` — also
used by Functions — share it), where Npgsql's periodic password provider refreshes the token every 45 minutes.
The migrator cannot hand DbUp a provider, so it resolves the token once up front
(`ResolveForDirectUseAsync`) and finishes well inside the token's lifetime. A raw `new NpgsqlConnection(...)`
or `UseNpgsql(string)` in production code is a build error (`BannedSymbols.txt`), because neither can
carry the token (taken from Gumnut, 2026-09-22, which found the `UseNpgsql` hole when its module contexts
were still built from the string).

**What this replaced:** the API alone used the data source; the job-run recorder, the outbox replayer and
DbUp opened raw connections and would have failed the moment the string had no password. The migrator's
`appsettings.json` also shipped a plaintext local password; it is gone, and the AppHost injects the string.

**Why not a password in Key Vault:** it is a secret to rotate, distribute and leak; the token is issued to the
managed identity by the platform, expires in an hour, and never exists in configuration. Deployment
templates (Aspire's `AddAzurePostgresFlexibleServer` in Entra mode) emit exactly the password-less shape.

**Re-add trigger for password authentication:** a PostgreSQL host with no Entra support — a self-hosted
deployment of a derived project. The code already handles it: a string with a password is used as given.
Do not add a second credential mechanism; extend the string shape rule if a third kind of host appears.

## The credential is decided by the shape of the configured value, for every Azure client (2026-09-20)

`AzureClientAuthentication` (ServiceDefaults) is `DatabaseAuthentication`'s counterpart for Blob Storage and
Service Bus: a value carrying a key (`AccountKey`, `SharedAccessKey`, `SharedAccessSignature`,
`UseDevelopmentStorage`, `UseDevelopmentEmulator`) is a connection string and is used as given; a bare
endpoint or namespace means "connect as the hosting identity" with a `DefaultAzureCredential`. Aspire injects
the keyed form for the local emulators and the endpoint form for deployed Azure resources under the *same*
configuration key, so the API's `ServiceBusClient` and the payload archive's `BlobServiceClient` are built
through it and nothing else changes between environments. The Functions host already follows the same rule
natively (`servicebus__fullyQualifiedNamespace`, `AzureWebJobsStorage__blobServiceUri`), which is why the
AppHost sets the bare `servicebus` connection string only in run mode.

**Re-add trigger for a keyed connection string in a deployed environment:** none. A deployment that cannot
use managed identity is not a supported deployment of this template.

## Managed identity everywhere, and what publish mode drops to get there (2026-09-20)

Deployed, every dependency is reached as the app's own managed identity and nothing in the environment holds
a credential: Azure Database for PostgreSQL with password authentication disabled, Azure Managed Redis with
access keys disabled and an access policy per identity, Service Bus and Storage through endpoint + identity,
the image pull through the environment's identity. To hold that line, publish mode differs from the local
rig in three places, all decided in the AppHost:

- **Seq is not published.** It has no identity story (unauthenticated) and a deployed environment already
  gets every log and trace over OTLP. Locally it stays.
- **Redis is Azure Managed Redis when published** (`AddAzureManagedRedis(...).RunAsContainer()`), the one
  line item this posture adds to a bill (Balanced B0, the smallest tier). A Redis container in the
  environment would need a password.
- **Keycloak's admin password is a generated secret parameter** rather than the local `admin`/`admin`.

Two deployment facts that only surfaced on a real environment, both pinned in the AppHost with comments:
the Functions host resolves a trigger connection from `ConnectionStrings:<name>` *before* the identity
settings, so the Functions resource must not `WithReference(serviceBus)` when published (it takes the role
assignment explicitly instead); and `ApplyAzureFunctionsConfiguration` fills `__fullyQualifiedNamespace`
with the endpoint URL where the extension wants the bare host. And the archive health check probes the
container, not the blob service's properties, because Storage Blob Data Contributor cannot read the latter.
A third, found on the first derived deployment: the ingress applies the operator allow-list to *every*
caller of a public hostname, other container apps included, so calls between the apps use the environment's
internal hostnames (`<app>.internal.<domain>`); Keycloak pins its issuer to the public name (`KC_HOSTNAME`,
backchannel dynamic) and the API fetches discovery from the internal one (`Identity:MetadataAddress`)
while validating the public issuer (`Identity:Authority`).

**Re-add trigger for any keyed credential in a deployed environment:** none. A service that cannot be
reached with a managed identity is replaced or left out, not given a key.

## Taken from derived projects

Improvements a derived project made to code the template also carries come back up here, translated onto the sample. Each port names the derived commits, so the next port starts after them.

- **Gumnut, 2026-09-28** (through `bf38ca4`). Taken: the payload-capture background writer (`888c257`, `edfdb98`; "Only the archive record is written before the response" above), the position that deleting a record leaves the archive to its retention (`474c7e6`, adapted to `DeleteCustomerCommand`), and `PagingConventionTests` (`87ff172`; the sample lists already paged, so only the rule came up). Not applicable here: the erasure journal and its re-apply (`c81c19c`, `f1e6b20`, `aeff6ae`; the template has no erasure command), the worker's shared client-credentials token (`e567f84`; the Functions worker never calls the API), in-process retries in the per-tenant scheduled job (`5bc8b6b`; the one timer job here is a single whole-job run that already records `Failed` and throws), the admin web's paging and unprivileged nginx (`40ad778`, `b96d950`; no web client), and per-test integration dates (`bf38ca4`; no static test day).
- **Gumnut, 2026-09-28** (`d2c3458`, `f79be32`). Taken: the scheduled-job watch. `JobWatchFunction` runs on `JobWatch:Cron` (every 15 minutes), reads each watched job's latest start, the outcome of its latest finished run and the first run left unfinished since then from `job_runs` (`IJobRunHistory`, registered by `AddJobRunRecording`), and judges them against the job's own schedule (Cronos, UTC as the timers run) on the `TimeProvider`: a last finished run that `Failed` is failing, and a next start more than `JobWatch:OverdueGraceMinutes` (15) past with no new run is overdue. Since 2026-10-03: a run that was cancelled is recorded as `Cancelled` and is failing like `Failed`; a run still unfinished `JobWatch:UnfinishedAfterMinutes` (120) after it started, with none finished since, is failing, so a job killed on every run is flagged though no run of it ever finishes; the schedule is whatever the `TimerTrigger` carries (a `%Section:Key%` setting or a literal; a six-field or five-field cron or an `hh:mm:ss` interval) on an instance or a static method; a schedule that cannot be read is that job's own Error line and the others are still judged; and a job that never ran is judged from when the watch first saw it, kept in `watched_jobs` (migration 0009), not from when this worker started, so a worker that restarts often still alarms. Each is one Error line in fixed words, and every pass ends with a `Job watch:` line, so silence is a signal; the alert rules are the hosting repository's, and `docs/runbooks/scheduled-jobs.md` gives their queries. Adapted: Gumnut lists its jobs by hand beside a convention test; here a timer function carries `[WatchedJob(name)]` and the watch discovers it with the setting its `TimerTrigger` names, so a derived project's new timer job is watched by being declared and configured, and `FunctionsHostConfigConventionTests` fails a timer without the attribute. Only `Failed` alerts: the cleanup's `Degraded` (time budget spent) keeps its own Warning, and the outbox processor's activity-window rows have no schedule and are not watched. The `starterapp.scheduled_job.runs` counter is recorded by the cleanup function, since there is no shared scheduled-job runner here, and host.json stops sampling traces so an Error line is not dropped. Rejected, as in Gumnut: a health endpoint over `job_runs` probed from outside, and per-job absence queries with the cron copied into the alert rules. **Re-add trigger** for a metric alert: the worker exporting its own telemetry to the alerting store.

## Considered and rejected

Recorded so future sessions do not re-propose them.

- **MediatR, AutoMapper, the repository pattern, an in-process background task queue.** Each conflicts with a documented prohibition or an existing mechanism (custom mediator, explicit mappers, DbContext directly, transactional outbox for anything that must survive a restart) The payload-capture background queue is not one: it holds only best-effort audit and index lines that may be dropped, never work that must survive a restart.
- **Production infrastructure as code (Bicep or azd).** Maintainer decision, 2026-06-10: deployment topology is owned by the hosting environment; Aspire is the only orchestration path in this repo. Since 2026-09-20 the AppHost *does* describe the Azure shape in publish mode (`AddAzureContainerAppEnvironment`, `AddAzurePostgresFlexibleServer(...).RunAsContainer()`, the migrator as a Container App job, Keycloak from `Realms/Dockerfile`); that is still Aspire, not IaC. The `azure.yaml`, environment values, resource group and ingress restrictions live in the hosting environment's repository, never here.
- **WORM immutability on audit blobs as a roadmap item.** Cannot be expressed here (the emulator does not enforce it and there is no IaC). Recorded instead as an accepted limitation with deployer guidance in `ARCHITECTURE_REVIEW.md`.
- **A `spikes/` folder convention.** Maintainer decision, 2026-06-10: experiments go through normal branches and worktrees.
- **Client-IP extraction chains in middleware.** The API runs behind a trusted edge; the edge owns client network identity.
- **List-query caching.** No pattern-based invalidation in `IDistributedCache`; revisit only with a versioned-namespace design (see the caching decision above).
- **TypeScript client codegen.** Marginal for an API-only template; the OpenAPI output already serves contract consumers.
- **A multi-issuer bearer path (`Authentication:Issuers[]`, a policy scheme forwarding on the token's unverified `iss`, per-issuer claim-name mapping).** Gumnut carries this shape (`BearerSchemes`, `ConfigureBearerSchemes`, `IdentityClaims` in ServiceDefaults) because one API there accepts staff, parent and Auth0 realms. Looked at on 2026-09-22 and left there: the template has one authority (`Identity:Authority`, with `Identity:MetadataAddress` for a discovery document that lives elsewhere), and every derived repo but Gumnut follows it. Re-add trigger: one API must accept tokens from a second issuer — take Gumnut's shape whole rather than registering a second `AddJwtBearer`. Left in Gumnut for the same reason (org- or product-specific, no caller here): `ResourceNaming` (every Azure resource named after its resource group), the landing-zone private endpoints, backups and standby parameters, the Docker Compose publish target, and the `SqlLike` / Dapper `DateOnly` handlers.
- **A request-row audit `action` stamp.** Captured before routing, the verb-derived value was wrong on exactly the override routes and duplicated `method`; the response row is the authoritative carrier (complexity review, 2026-06-12).
