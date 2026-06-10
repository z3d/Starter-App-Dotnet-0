# .NET 10 Clean Architecture Template

Architectural patterns, conventions, and standards for a .NET 10 project using Aspire orchestration. Detailed implementation guides are in `.agents/skills/`.

## Project Overview

**Clean Architecture** .NET 10 solution implementing:
- **Minimal APIs**: Endpoint-based architecture with `IEndpointDefinition` auto-discovery
- **CQRS Pattern**: Separate command/query responsibilities
- **Domain-Driven Design**: Rich domain models with business logic
- **Aspire Orchestration**: Service orchestration and observability
- **Reproducible Builds**: Package lock files and centralized configuration

### Solution Structure

```
Solution Root/
├── src/
│   ├── [ProjectName].Api/              # Minimal API Layer
│   │   ├── Endpoints/                  # API endpoint definitions
│   │   │   ├── CustomerEndpoints.cs
│   │   │   ├── OrderEndpoints.cs
│   │   │   ├── ProductEndpoints.cs
│   │   │   ├── IEndpointDefinition.cs
│   │   │   ├── EndpointExtensions.cs
│   │   │   └── Filters/
│   │   ├── Application/                # CQRS commands and queries
│   │   ├── Data/                       # EF Core DbContext
│   │   └── Infrastructure/             # Cross-cutting concerns (outbox, mediator)
│   ├── [ProjectName].Domain/           # Domain Layer (Core Business Logic)
│   ├── [ProjectName].Functions/        # Azure Functions (Service Bus subscribers)
│   ├── [ProjectName].AppHost/          # Aspire Orchestration Host
│   ├── [ProjectName].AppHost.Tests/    # Aspire Integration Tests (DistributedApplicationTestingBuilder)
│   ├── [ProjectName].ServiceDefaults/  # Shared Aspire Service Configuration
│   ├── [ProjectName].DbMigrator/       # Database Migration Tool
│   └── [ProjectName].Tests/            # Unit, Convention, Integration, Fuzzing Tests
└── docs/                               # Documentation
    └── API-ENDPOINTS.md
```

## AI-Agent Maintenance Context

This codebase is maintained by AI agents. Design decisions favour **mechanical rules over architectural taste**:
- Convention tests enforce structural rules that agents follow perfectly — ambiguity is the real risk, not boilerplate
- Every command and query must have a validator (enforced by convention test), even trivial ones — agents generate boilerplate cheaply, and skipping coverage creates judgment calls that cause drift
- **Always assume concurrent sessions:** multiple AI agents or human sessions may be editing this codebase at the same time. Before changing files, check the working tree, treat unfamiliar edits as someone else's work, keep changes scoped, and never revert or overwrite changes you did not make unless explicitly instructed.
- **Work in your own git worktree, not the shared checkout.** Because several sessions share this repo at once, the primary checkout may be on someone else's feature branch with their uncommitted changes — do **not** switch its branch, stage, or commit there (your commit will land on their branch and you risk sweeping their staged work into it). Instead spin off a dedicated worktree from the up-to-date remote and do your work there: `git fetch origin && git worktree add -b <your-branch> ../<dir> origin/main`. Commit in the worktree, push it, and fast-forward `main` from it (`git push origin <your-branch>:main` when it's a clean child of `origin/main`), then `git worktree remove`. This keeps your branch and others' in-flight work from colliding. (Doc-only commits in a fresh worktree can't run the build/test pre-commit hook — it has no build artifacts — so `--no-verify` is acceptable for Markdown-only changes there.)
- **Example:** The validator coverage rule was a deliberate trade-off. For human-maintained code, requiring a validator for `DeleteProductCommand` (just `Id > 0`) would be busywork. For agent-maintained code, the mechanical rule eliminates the question "does this command need a validator?" and the convention test catches missing validators on every build.

### Validator–Domain Guard Sync Rule

Validators and domain guards intentionally overlap (defense-in-depth). When modifying either:
- **Adding/changing a domain constructor guard or value object validation** → update the corresponding `IValidator<T>` to match
- **Adding/changing a validator rule** → verify the domain guard covers the same invariant as a safety net
- Domain guards throw single exceptions (last line of defense). Validators yield structured multi-error `ValidationError` collections (API UX).

## Architecture Principles

### Core Design Principles

**Domain-Driven Design**
- Rich domain models: entities contain business behavior, not just properties
- Value objects: immutable objects with business meaning (Email, Money)
- Aggregate roots control access and maintain consistency
- Aggregate roots may raise domain events; persistence captures them into the outbox in the same unit of work
- Aggregate roots own child collections via backing fields; EF Core populates via `.Include()`
- `Reconstitute` is internal/test-only — production handlers use tracked EF entities
- **Aggregate ID convention**: aggregates that raise creation events (override `RecordCreation()`) MUST use client-generated `Guid.CreateVersion7()` IDs, assigned in the constructor. Aggregates without creation events MAY use database-generated integer IDs. Reason: `ApplicationDbContext` captures outbox messages inside a single `SaveChanges` — creation events are captured BEFORE the save, so Ids must be known client-side. This keeps `EnableRetryOnFailure` safe: no user transaction, retry is transparent. A convention test (`AggregatesOverridingRecordCreation_MustHaveGuidId`) enforces the rule.
- **Create-order retry idempotency**: `CreateOrderCommandHandler` generates one stable order Id before entering the EF execution-strategy retry delegate. Each retry checks for that Id before reserving stock. Do not move Id generation back inside the retry delegate; commit-unknown retries could create a second order and reserve stock twice.
- **Optimistic concurrency**: concurrency-critical entities (`Order`, `Product`) use PostgreSQL `xmin` row version tokens configured with `IsRowVersion()`. Stale order state changes or inventory writes must fail with `DbUpdateConcurrencyException`, which the API maps to `409 Conflict`.

**CQRS Implementation**
- Commands → EF Core (ApplicationDbContext) → return DTOs
- Queries → Dapper (IDbConnection) → return ReadModels
- Never mix: no DTOs from queries, no ReadModels from commands
- Custom mediator (not MediatR) with auto-registration via `builder.Services.AddMediator()`
- Pipeline behaviors wrap handler invocation (currently: `CachingBehavior` for `ICacheable` queries)

**Distributed Caching**
- Redis via Aspire `AddRedis` / `AddRedisDistributedCache("redis")` — falls back to in-memory cache when Redis connection string is absent (tests, standalone dev)
- Queries opt in by implementing `ICacheable` (provides `CacheKey` and `CacheDuration`)
- `CachingBehavior` in the mediator pipeline checks cache before handler, stores on miss, skips null results
- Command handlers invalidate specific entity keys via `ICacheInvalidator` after `SaveChangesAsync`
- Only by-id queries are cached — list/collection queries are NOT cached because `IDistributedCache` has no pattern-based deletion, and stale list pages after writes are user-visible bugs. If list caching is needed later, use a versioned namespace approach.
- Convention tests enforce: non-empty cache keys, positive durations, deterministic identity keys, by-id-only caching, and invalidator injection for non-create mutations on cacheable entities

**Payload Archive / PII Audit**
- Every inbound and outbound payload is captured through the shared payload capture service: HTTP request/response bodies, outbound Service Bus domain-event messages, and inbound Service Bus Function messages
- Orchestration-owned infrastructure such as Blob storage must be wired in `AppHost`; Aspire is the only supported local orchestration path
- Archive blobs are correlation-bound JSONL streams under `archive/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl`; all operations for the same correlation id in that minute append to the same archive file
- Audit blobs are time-window JSONL streams under `audit/{yyyy-MM-dd}/{HH}/{mm}/payload-audit.jsonl`; each audit row includes timestamp, correlation id, operation metadata, archive blob name, payload hash, and the full payload
- Entity index blobs are pointer-only support indexes under `entity-index/{entityType}/{entityId}/{yyyy-MM-dd}/{HH}/{mm}/{correlationId}.jsonl`; they must not duplicate the full payload, only metadata and archive/audit blob names
- Entity-reference extraction consults `SensitivePropertyNames` (the same normalized-substring semantics as the JSON redactor) and requires a real `Id`/`_id` suffix, so sensitive `*Id` values (e.g. `nationalId`) never become blob path segments or logged entity-index names
- Append-blob writes are atomic per record: a record whose JSONL line exceeds one 4 MiB append block is written to a single-writer `<blobName>.oversize-<id>.jsonl` sidecar (same date/hour/minute path, so retention covers it) and the shared stream gets a small pointer line (`oversizeRecord: true`) instead — multi-block appends into a shared blob could interleave with concurrent writers and splice records
- Blob archive/audit are full-fidelity support artifacts and may contain PII. Logs must remain redacted: use the shared JSON payload redactor plus `Serilog.Enrichers.Sensitive`; never log raw `{Body}` values directly. Payload capture logs must include archive, audit, and entity-index blob file names so support agents can jump from logs to artifacts.
- Payload capture has an explicit **per-channel** failure policy because an audit sidecar must not take down synchronous user traffic: `PayloadCapture:HttpFailureMode` (default `FailOpen` — a capture failure on the HTTP path is logged and the request proceeds) and `PayloadCapture:ServiceBusFailureMode` (capture failure on the outbox/Service Bus path). Both default to `FailOpen` in code so standalone dev/tests with no archive store never break. Production-like orchestrations set `RequireArchiveStore=true` and `ServiceBusFailureMode=FailClosed` (HTTP stays `FailOpen` so archiving never fails user requests; a compliance domain can opt the HTTP path into `FailClosed`). Under `FailClosed`, capture is enforced even when the archive store is absent (the null-store path honors the channel's mode), and `OutboxProcessor` treats a capture failure as **pause-the-batch** (the not-yet-published event is retried once the archive recovers) rather than poisoning the message — so no event is published without a durable audit record, and none is permanently lost.
- HTTP payload capture is bounded by `PayloadCapture:MaxPayloadBytes` and `CapturedContentTypes`; truncated or skipped payload records include metadata so support can distinguish full-fidelity artifacts from bounded captures.
- **Recorded decision — capture runs first.** `UsePayloadCapture()` is deliberately the first middleware, ahead of exception handling, gateway identity, and rate limiting: rejected traffic (401/403/429, 404 junk) is captured by design as part of the full-fidelity audit posture. Request-path amplification is bounded by `MaxPayloadBytes`, `CapturedContentTypes`, and `MaxEntityReferences`; total inbound volume is the upstream gateway's responsibility. Do not move capture behind the rate limiter without a new recorded decision — it would blind the audit trail to rejected traffic. Response capture runs on an unlinked token (and still runs when the client aborts) so a deliberate disconnect cannot suppress the audit record.
- `X-Correlation-ID` is accepted on HTTP requests, echoed in HTTP responses, persisted on outbox rows, and propagated to Service Bus `CorrelationId` and application properties. Under signed gateway identity the correlation id is **contract-bound**: it must be `[A-Za-z0-9._-]{1,128}` (the gateway normalizes trace ids — e.g. `:`-delimited ones — before forwarding). `GatewayIdentityHeaders.Read` rejects out-of-charset/over-length values up front rather than silently rewriting them, because the gateway signs the assertion over the *raw* correlation id and includes it in the projected-header hash — sanitizing only on the verify side would compare against a value the signer never saw and reject valid traffic. `PayloadCaptureMiddleware` therefore leaves a *present* correlation id on the request unchanged (only injecting a generated one when the caller sends none) so the gateway layer validates the exact signed value; its echoed/archived id stays sanitized so no raw client input is reflected. Keep the verifier's stored value identical to the signed value, and do not re-introduce request-header sanitization ahead of the gateway middleware. Lossy sanitization (stripped characters or truncation) appends a short raw-bound hash suffix so distinct raw ids never collapse into one archive stream.
- `PayloadArchiveCleanupFunction` is timer-triggered from the `PayloadCapture:CleanupCron` configuration key (supplied via the `PayloadCapture__CleanupCron` environment variable — trigger `%...%` lookups must use the `:` config-key form because the env provider normalizes `__`, and a convention test enforces this). The Functions image bakes an hourly default so a missing setting cannot fail function indexing and take down the Service Bus subscribers in the same worker. It deletes archive, audit, and entity-index blobs older than `PayloadCapture:RetentionDays` by parsing the date/hour/minute path

**Authentication**
- This API assumes it runs behind APIM or an equivalent trusted gateway that validates caller auth — do not add ASP.NET authentication/JWT bearer middleware to the API itself
- The gateway must strip inbound `X-Authenticated-*` and `X-Gateway-Assertion` headers, then project a small normalized identity contract: `X-Authenticated-Subject`, `X-Authenticated-Principal-Type`, `X-Authenticated-Tenant-Id`, `X-Authenticated-Scopes`, optional `X-Authenticated-Amr`, and `X-Correlation-ID`
- Production-like environments must use `GatewayIdentity:Mode=Required` with a signed `X-Gateway-Assertion`; `UnsignedDevelopment` is only for Development and Testing
- `X-Gateway-Assertion` signs issuer, audience, subject, principal type, tenant, scopes, correlation id, method, path, lifetime, key id, and a hash of the projected identity headers including authentication methods. Missing, expired, tampered, wrong-audience, wrong-path, or wrong-key assertions return `401`
- Endpoint groups under `/api/v1` must call `RequireGatewayIdentity()`, and every `/api/v1` route must declare its required gateway scope with `RequireScope("domain:read|write")`. Every non-GET `/api/v1` route must also call `SecuredBy2Fa()`, which requires the gateway-projected authentication methods (`X-Authenticated-Amr`) to include `mfa`. Convention tests enforce this from mapped endpoint metadata, and production code must use `ICurrentUser` instead of reading raw identity headers directly
- Customer, Product, and Order are owner-scoped resources. Create handlers stamp `OwnerSubject` and `TenantId` from `ICurrentUser`; query handlers filter by that owner scope; mutation handlers call `IOwnerOnlyPolicy` before changing or deleting a loaded aggregate. Cross-owner reads are hidden as not found or empty lists, while cross-owner mutations return `403 Forbidden`. Policy invocation is structurally verified, not just convention-tested: non-create commands implement `IOwnerAuthorizedMutation`, `OwnerOnlyPolicy.Authorize` records its evaluation on a scoped `OwnerPolicyEvaluationTracker`, and `OwnerAuthorizationBehavior` in the mediator pipeline asserts after a marked command completes that the policy was actually consulted — throwing in Development/Testing (so the test suite catches a handler that injects the policy but never calls it) and logging an error in production (the mutation is already persisted; failing the response would not undo it). Convention tests keep the marker cohort complete (every non-create command) and commands-only.
- Owner-only authorization is intentionally application-layer behavior, not endpoint attributes or route metadata. Endpoint metadata can enforce gateway identity, scope, and MFA before dispatch, but it cannot know the persisted owner of a specific resource. Keep owner checks in query predicates and command handlers where the owner columns are available.
- All resource queries must implement `IOwnerScopedRequest`, and all command/query handlers must inject `IOwnerOnlyPolicy`; convention tests enforce both rules so future endpoints cannot drift back to global visibility
- Owner-scoped by-id caches include the verified tenant/subject in the cache key. Mutations invalidate both the legacy resource key and the owner-scoped key so cached data cannot cross identities.
- Gateway auth does not eliminate API authorization. Tenant ownership, resource-level permissions, and domain-sensitive workflow rules still belong in application/domain code, with `IOwnerOnlyPolicy` as the minimum baseline for owned resources.
- Rate limiting partitions by the verified tenant/subject identity for protected endpoints and falls back to IP only for public/unauthenticated requests

**Clean Architecture**
- Domain layer has no external dependencies
- Interface segregation: small, focused interfaces
- Explicit over magic: prefer explicit code over convention-based patterns

### Prohibited Anti-Patterns

**NEVER use these libraries:**
- AutoMapper — use explicit mapping code
- MediatR — use custom mediator (commercial licensing)
- Commercial libraries without explicit stakeholder approval

**NEVER use these patterns:**
- Anemic domain models — domain objects must have behavior
- Mixed CQRS concerns — keep commands and queries strictly separate
- Repository pattern — use DbContext directly in command handlers
- Explicit transactions — use EF Core navigation properties and single `SaveChangesAsync` instead. If you need two saves, the aggregate boundary is wrong.
- `AsNoTracking` + `Update` in command handlers — marks all columns modified, creates lost-update risks. Load tracked entities instead.
- Public `SetId()` methods on entities — EF Core sets Id via private setter. No identity mutation methods.
- Code regions, historical comments, XML doc comments (for app code)
- Dual representation (separate entity/value object pairs)

**Code quality:**
- No tight coupling — use DI and interfaces
- No missing validation — validate at domain and API boundaries
- Follow established naming conventions strictly
- Prefer .NET native libraries over third-party dependencies

## Build Configuration

### Centralized Configuration (Directory.Build.props)
- Target: .NET 10.0, nullable reference types, implicit usings
- Treat warnings as errors, `AnalysisMode=All` with a curated `.editorconfig` policy, global analyzers enabled, and code style enforced during build
- Analyzer severity policy is intentional: prefer `.editorconfig` severity configuration with a documented reason over scattered `#pragma` suppressions. Do not mass-apply public-to-internal churn, `ConfigureAwait(false)`, XML doc comments, or DTO collection-shape changes just to satisfy broad analyzer rules.
- XML documentation output is enabled only to allow build-time `IDE0005` unused-using enforcement; missing XML comment warnings stay suppressed because app code should not add XML doc comments
- Package lock files for reproducible builds (`RestorePackagesWithLockFile=true`)

### Central Package Management (Directory.Packages.props)
All NuGet package versions are pinned in `Directory.Packages.props` at the repo root. Individual `.csproj` files contain `<PackageReference Include="X" />` with **no `Version=` attribute** — the version comes from the central file.

**When adding a new package:**
1. Add `<PackageVersion Include="X" Version="Y" />` to `Directory.Packages.props`
2. Add `<PackageReference Include="X" />` (no version) to the consuming `.csproj`
3. Run `dotnet restore --force-evaluate` to update lock files

Never put `Version=` on a `<PackageReference>` — CPM will error on the downgrade/mismatch. Never duplicate a `<PackageVersion>` across multiple entries for the same package. The analyzer `<GlobalPackageReference>` also lives in `Directory.Packages.props` (CPM requires it there, not in `Directory.Build.props`).

**Lock-file exemption for Aspire host projects (`StarterApp.AppHost`, `StarterApp.AppHost.Tests`):** these two projects set `<RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>` and have **no** `packages.lock.json` (also git-ignored). The Aspire SDK injects host-RID-specific *Direct* packages (`Aspire.Dashboard.Sdk.<rid>`, `Aspire.Hosting.Orchestration.<rid>`) chosen by whichever machine runs `restore` — so a committed lock file gets `osx-arm64` from a Mac, `win-x64` from Windows, `linux-x64` in CI. No single lock file satisfies `dotnet restore --locked-mode` on all platforms, which caused **recurring CI churn**: every developer's restore flipped the RID and broke Linux CI. Exempting only these two projects (the other six lock files are RID-agnostic and stay locked) ends the churn while preserving reproducible locked restore everywhere else. Do **not** re-add lock files to these projects or switch CI back to `--force-evaluate` to "fix" a future occurrence — the exemption is the fix.

### Supply-Chain Hardening
- **Feed restriction (`NuGet.config`)**: a repo-root `NuGet.config` `<clear/>`s inherited sources, declares only nuget.org, and uses `<packageSourceMapping>` to bind every package id (`*`) to that source — the primary dependency-confusion defense. It is COPY'd into all three Docker builds so container restores honor it. **Signature validation (`signatureValidationMode=require` + `<trustedSigners>`) is deliberately NOT enabled**: trusting only the current nuget.org repository cert breaks restore on older packages carrying a pre-rotation countersignature (e.g. `System.Security.Cryptography.ProtectedData 4.5.0` → NU3034), and enforcement differs by OS (passes macOS, fails Linux/Docker). Tamper-detection is already covered by lock-file content hashes + locked-mode. Do not re-add it without testing a clean-cache Linux restore across the full package set.
- **Locked-mode is the default everywhere** (not just CI): `RestoreLockedMode` is set whenever the property is unset (`Directory.Build.props`), so a plain local `dotnet restore` cannot silently rewrite `packages.lock.json`. Intentional package add/upgrade uses `dotnet restore` with the force-evaluate flag. Inert on the lock-file-exempt AppHost projects.
- **Docker base images are pinned by immutable `@sha256` digest** (not mutable tags) in all three Dockerfiles, keeping the OS/runtime layer reproducible under the digest-pinned managed dependencies and activating the Dependabot docker updater (which cannot bump a bare tag). Resolve a digest with `docker buildx imagetools inspect <image>`. A `.dockerignore` keeps `bin/`/`obj/`, local dev config/secrets, and test/orchestration projects out of the build context (Docker does not honor `.gitignore`).
- **SDK pin (`global.json`)**: pins the SDK band (`rollForward: latestFeature`, `allowPrerelease: false`) for a reproducible toolchain floor.
- **Secret scanning**: the `secret-scan` workflow installs a SHA-256-checksum-verified, version-pinned `gitleaks` binary (no unpinned marketplace action, license-free) and scans full git history on push/PR; intentional placeholders are allowlisted in `.gitleaks.toml`. Local dev settings (`appsettings.Development.json`) are git-ignored with a tracked `.example` template — never commit real secrets to the tracked tree.
- **CI trust**: every third-party GitHub Action is pinned to a commit SHA (not a tag), workflows declare least-privilege `permissions: contents: read`, and Dependabot covers nuget + github-actions + docker. `SupplyChainConventionTests` mechanically enforces the digest-pin, feed-mapping, and SDK-pin rules so they cannot drift.

### Code Formatting (.editorconfig)
- 180 char line length, file-scoped namespaces, system usings first
- StyleCop rules: SA1200, SA1209, SA1210, SA1211
- Prefer `GlobalUsings.cs` per project over per-file using directives
- CI runs `dotnet format --verify-no-changes --verbosity minimal --no-restore` before build

## Commit Discipline

**ALWAYS commit or ask to commit after completing a task.** Do not leave uncommitted changes in the working tree. If unsure whether the user wants a commit, ask. Uncommitted work is invisible to other agents and at risk of being lost.

## Pre-Commit Checklist

**ALWAYS** complete before committing:
1. `dotnet format` — apply formatting standards
2. `dotnet build` — ensure compilation success
3. `dotnet test` — verify all tests pass
4. `dotnet restore` — update lock files if dependencies changed
5. Ensure `packages.lock.json` files are committed

## Key Patterns

**DDD Entities**: Private setters, protected EF Core constructor, public domain constructor with validation, domain methods for mutations. See `.agents/skills/ddd-implementation/SKILL.md`.

**CQRS Handlers**: Commands load tracked entities via DbContext with `.Include()`, mutate through domain methods, single `SaveChangesAsync(cancellationToken)`. Queries use `IDbConnection` with Dapper SQL. Convention tests enforce this separation. See `.agents/skills/cqrs-patterns/SKILL.md`.

**Data Access**: EF Core with Npgsql/PostgreSQL, `OwnsOne` for value objects, `IsRowVersion()`/`xmin` concurrency tokens on order/inventory entities, per-entity `IEntityTypeConfiguration<T>` classes under `Data/Configurations/`, and DbUp migrations in DbMigrator project. `ApplicationDbContext` uses `ApplyConfigurationsFromAssembly()` so entity mapping remains mechanically discoverable and consistency-testable. See `.agents/skills/data-access/SKILL.md`.

**Domain Events / Outbox / Service Bus**: Domain events are raised inside aggregates and persisted to the `outbox_messages` table by `ApplicationDbContext` during a single `SaveChangesAsync`. Creation events use the `AggregateRoot.RecordCreation()` override pattern — called BEFORE `SaveChanges` because aggregates that raise creation events carry client-assigned `Guid.CreateVersion7()` Ids. Single-SaveChanges means no user transaction is required, which keeps `EnableRetryOnFailure` safe for PostgreSQL transient faults. The `OutboxProcessor` BackgroundService claims unprocessed messages in a short PostgreSQL transaction using `ProcessingId`/`LockedUntilUtc` plus `FOR UPDATE SKIP LOCKED`, publishes them to Azure Service Bus (topic: `domain-events`) outside the lock, then marks outcomes afterward. Azure Functions subscribe via topic subscriptions with correlation filters (`email-notifications`, `inventory-reservation`); AppHost runs them through the Functions Docker runtime so trigger listeners are active without extra local Functions tooling. The processor marks messages as processed or errored — errored messages are skipped on subsequent polls. Errored rows are recoverable via the sanctioned replay verb (`dotnet run --project src/StarterApp.DbMigrator -- replay-outbox --id <guid>` or `--all-errored`), which resets error/claim state and stamps `replay_count`/`replayed_on_utc`; the processor marks replayed publishes with `Replay`/`ReplayCount` application properties so audit distinguishes republish from first delivery. Dead-lettered subscription messages follow `docs/runbooks/event-replay.md` (re-submit to the topic; archived payloads are the fallback source); batch outcomes are saved resiliently (a row whose claim was stolen by another replica is detached so the rest of the batch's outcomes persist). The processor also purges processed/errored rows older than `OutboxProcessor:RetentionDays` (default 30) so full event payloads do not accumulate forever. Service Bus registration is conditional: no-op when `ConnectionStrings:servicebus` is absent, but **only in Development/Testing** — other environments fail startup loudly so a typo'd connection string cannot silently disable eventing. The Service Bus emulator topology is defined in AppHost fluent configuration with duplicate detection enabled (5-minute window, the emulator maximum), 24-hour topic/subscription TTLs, and `DeadLetteringOnMessageExpiration` on every subscription so consumer downtime dead-letters events for replay instead of silently deleting them (convention-tested). The 24h TTL applies to **publish mode only**: the Service Bus emulator crash-loops on any TTL above 1 hour ("Max DefaultMessageTimeToLive supported 1h", container exit 139), so run mode clamps every TTL via `ServiceBusTopology.ClampForEmulator` (convention-tested) — do not assign the 24h constants to emulator topology directly. The Functions host pairs this with an exponentialBackoff retry policy in host.json sized to fit inside the lock-renewal window, so a transient capture/blob outage backs off instead of burning `MaxDeliveryCount` in seconds. There is no ordering guarantee into subscribers (`maxConcurrentCalls: 16`, no sessions) — subscriber implementations must tolerate out-of-order delivery.

**Event Contracts**: Each `IDomainEvent` implementation exposes a stable, versioned `EventType` property (e.g. `order.created.v1`) via a `const Contract` field. `OutboxMessage.Create` persists `domainEvent.EventType` — **not** the CLR type name. This decouples event routing from class names: renaming a C# class does not break Service Bus subscriptions or existing outbox rows. Convention tests enforce: every event has a non-empty contract, contracts are unique, and Service Bus subscription filters reference valid contracts. When adding a new domain event, give it a `const Contract` and implement `EventType => Contract`. The serialized *shape* of every contract is also pinned: `EventContractSnapshotTests` renders each event through the real `OutboxMessage.Create` path and diffs against fixtures in `src/StarterApp.Tests/Contracts/snapshots/` — a property rename/removal/reorder under the same contract id fails the build with a pinned-vs-actual diff. Update fixtures deliberately with `UPDATE_EVENT_SNAPSHOTS=1 dotnet test --filter EventContractSnapshot` and decide between a compatible change and a new `.v2` contract; new events need a representative instance + fixture (completeness is test-enforced).

**Database Migrations**: Migrations run exclusively via the dedicated `DbMigrator` console app — never embedded in API startup (eliminates race conditions with multiple replicas).
- **Aspire:** `AppHost` runs `DbMigrator` with `WaitFor` dependency on PostgreSQL
- **Container deployments:** run the `DbMigrator` image/job to completion before starting API replicas
- **Standalone dev:** Run `dotnet run --project src/StarterApp.DbMigrator` before starting the API
- **Integration tests:** `TestFixture.RunDbUpMigrations()` runs migrations independently
- **Constraint naming**: Every constraint must have an explicit name — no anonymous/system-generated names. Convention: `pk_table`, `fk_table_column`, `df_table_column`, `ck_table_description`, `ix_table_column`. This makes future migrations deterministic (`DROP CONSTRAINT pk_orders`). Enforced by convention tests from the first migration onward.

**Testing**: Two test projects:
- `StarterApp.Tests` — xUnit + FsCheck property-based testing + Best.Conventional conventions (including `CachingConventionTests`, `DapperConventionTests` for SELECT * prevention via IL inspection, `DateTimeOffset` enforcement, constraint naming enforcement). Uses WebApplicationFactory + Testcontainers for integration tests. See `.agents/skills/testing-strategy/SKILL.md`.
- `StarterApp.AppHost.Tests` — Aspire integration tests using `DistributedApplicationTestingBuilder`. Spins up the full distributed app (PostgreSQL, Service Bus emulator, API, Functions) to test the end-to-end pipeline. Tag slow tests with `[Trait("Category", "Aspire")]`. See `.agents/skills/testing-strategy/SKILL.md`.

**Performance Testing (k6)**: `tests/k6/` holds a k6 suite run manually against an Aspire-started API (`K6_BASE_URL=https://localhost:<api-port>`): `smoke.js` (functional smoke — all checks pass, zero HTTP errors, p95 under 2s) and `load.js` (two concurrent ramping-VU scenarios — browse to 200 VUs, order placement to 50 VUs — with global thresholds p95 < 500ms / p99 < 1500ms / HTTP error rate < 1% / check pass rate > 99%, plus per-endpoint p95 thresholds via scenario tags). Scripts send the normalized `X-Authenticated-*` gateway identity headers (including `X-Authenticated-Amr` with `mfa` for write routes) so the protected `/api/v1` surface is reachable under `GatewayIdentity:Mode=UnsignedDevelopment`. CI gating: `.github/workflows/perf.yml` runs nightly (02:00 UTC) plus on dispatch via `tests/k6/run-perf.sh`, which boots a throwaway PostgreSQL, migrates, bulk-seeds owner-scoped data for the k6 identity (`tests/k6/seed/perf-seed.sql` — 20k customers/products/orders so list/pagination/index paths run at realistic volume; schema drift is caught by `PerfSeedScriptTests`), starts the API standalone, and fails the run on any threshold breach. List checks also enforce a volume floor (`K6_MIN_LIST_ROWS`, set to 20 in the gate) so a fast-but-empty response cannot pass. See `tests/k6/README.md`.

**Consistency Measurement**: `StarterApp.Tests/Consistency/` is advisory, not a build policy surface. It measures three extensible cohorts against pinned exemplars in `docs/exemplars/`: command handlers, query handlers, and EF configurations. Use it to detect structural drift and surprising outliers; put deterministic rules in convention tests.

**API Design**: Minimal APIs with `IEndpointDefinition` pattern, auto-discovery, endpoint filters for route-specific logic. See `.agents/skills/api-design/SKILL.md`.

**Health Endpoints**: Expose `/health` for aggregate status, `/health/ready` for readiness (including database connectivity), and `/health/live` plus `/alive` for liveness. Docker and container platforms should use readiness for traffic gating and liveness for restart decisions.

**Security Scanning (DAST)**: `dast/run-dast.sh` runs an OWASP ZAP dynamic scan (`dast/automation.yaml`) — it boots a throwaway PostgreSQL, applies migrations, starts the API in `GatewayIdentity:Mode=UnsignedDevelopment` (injecting the projected gateway identity headers so the protected `/api/v1` surface is reachable), then imports the OpenAPI doc, spiders, and runs passive + active scans, failing the build on any alert at or above `FAIL_RISK` (default `Medium`). Reports land in `dast/reports/` (git-ignored). Requires Docker + .NET SDK + `jq`. ZAP false positives are suppressed narrowly via scoped `alertFilter` entries in the plan (e.g. rules 90022/10023 on `/openapi/v1.json`, whose body contains documented `ProducesProblem(500)` "Internal Server Error" text; rule 6 Path Traversal on the int-typed `page`/`pageSize` pagination params, which model binding rejects non-numeric payloads for before any handler runs) — never by widening scan exclusions. See `dast/README.md`.

**Pagination**: List endpoints use `page`/`pageSize` query params with PostgreSQL `LIMIT/OFFSET`. Handlers fetch `pageSize + 1` rows; endpoints trim the extra row and return a `PagedResponse<T>` envelope (`{ data: [...], hasMore: true/false }`). This avoids expensive COUNT queries. Total count is a UI concern — if a frontend needs it, add a separate count endpoint rather than embedding it in every list response.

**Debugging**: Always reproduce with a failing test first, get full stack trace, fix root cause not symptoms. See `.agents/skills/development-workflow/SKILL.md`.

## Documentation Maintenance

- Update AGENTS.md with every architectural change
- Document WHY not WHAT
- Keep subsidiary docs in sync: `README.md`, `ASPIRE_SETUP_COMPLETE.md`, `docs/API-ENDPOINTS.md`, `docs/01-dotnet-setup/`, `docs/03-docker-setup/`, `docs/05-aspire-setup/`
- Keep `AGENTS.md`/`.agents/skills/**` and `CLAUDE.md`/`.claude/skills/**` synchronized. Any drift must be intentional and limited to agent-specific names or paths (for example `AGENTS.md` vs `CLAUDE.md`, `.agents/skills` vs `.claude/skills`); document the reason near the drift if it is not obvious. `AgentDocsConventionTests` enforces this mechanically: both skill trees must contain the same files, and every mirrored pair (including the two root docs) must be identical after swapping those agent-specific tokens — each file leads with its own doc/skills names. Any other difference fails the build; a genuinely harness-specific difference requires extending the test's canonicalization with a documented reason.
- **Architecture review (`docs/ARCHITECTURE_REVIEW.md`)**: Read this before starting any review or hardening task — it contains prior findings and current score. After fixing issues, update the doc: mark findings as resolved, add any new findings to the "Open Findings" section, and adjust the score. This is the shared artifact that keeps multiple agent conversations in sync.
- When fixing an architecture review finding, add focused regression tests for the failure mode before marking it resolved. If a finding cannot be covered directly, document the residual test gap in `docs/ARCHITECTURE_REVIEW.md`.

## Development Commands

```bash
dotnet format                                           # Format code
dotnet build                                            # Build solution
dotnet test                                             # Run all tests
dotnet restore --use-lock-file                          # Update lock files
dotnet restore --locked-mode                            # CI/CD locked restore
DEV_TUNNEL_ACK_UNSIGNED_API=true dotnet run --project src/StarterApp.AppHost -- --devtunnel  # Run with dev tunnel (ack required: tunneled API is UnsignedDevelopment)
act                                                     # Run CI locally (flags in .actrc)
```
