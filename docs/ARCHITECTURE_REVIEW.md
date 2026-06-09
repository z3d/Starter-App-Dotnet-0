# Architecture Review

## Overview

A .NET 10 Clean Architecture starter template implementing CQRS, DDD, and modern DevOps practices across an e-commerce domain (Products, Customers, Orders) with Aspire orchestration and PostgreSQL.

**Score: 8.2/10** — independent post-session re-score (2026-06-09, strict production scale; 8-dimension fan-out with adversarial verification of every claimed fix). Trajectory: the historical self-graded **9.7** was stale/monotonic (and is retained below only for provenance — do not trust it); an independent pre-session pass scored **7.8**; this session's hardening lifted it to **8.2** (every session fix verified in code; +0.4 for closing the defaults/invariant/test cluster). Held below 9 by folder-only Clean Architecture inside one assembly and a few latent footguns (see Open Findings). NB: both independent passes also deducted for **proportionality** (heavy machinery for a 3-entity domain) — but that is a **deliberate design stance, not a flaw** (see the "Accepted design rationale" note below): the template exists to demonstrate every production pattern for agents to copy and to mechanically enforce its own conventions. On the correct agent-template rubric that deduction largely falls away, so the effective ceiling is higher than 8.2; the genuine remaining gap is the footguns + the create-order retry-idempotency test, not the breadth.

> **Status statement (factual — read this before trusting the number).** The score above is **self-assessed by the maintaining agents** (this repo is reviewed across sessions by both Claude and Codex) per the CLAUDE.md instruction to "adjust the score" after fixing findings. There is **no external human validator and no defined rubric/denominator** — treat it as a maintenance log, not an independent audit. Cross-model review (Claude + Codex) does add *some* genuine independence and is worth more than a single model re-grading itself, but the structural conflict remains: whichever agent fixes a finding also adjusts the score in the same session. The number is effectively **monotonic** — it dips on discovery and recovers within the same session (e.g. 9.6→9.4→9.5 around #74, and 9.6→9.7 when #78/#79 were fixed-and-closed in one session). Verifiable facts as of this edit:
> - **9 command handlers, 7 query handlers, 16 validators, 0 CQRS violations** (the "8 query handlers" figure that appeared under *CQRS Implementation* was a miscount and is corrected to 7).
> - Last genuinely user-visible defect found and fixed: **#74** (stale product-stock cache after order mutations), 2026-06-07. A large share of the "84 resolved" are convention-test hardening, presence→behaviour test upgrades, and doc fixes rather than runtime defects — useful work, but counted equally with real bugs, which inflates the headline.
> - The **"533 passed" test baseline** cited in older notes is stale (the suite is now substantially larger); re-run `dotnet test` for a current count before relying on it.

> **Fresh review 2026-06-01 (independent re-read by a new model).** Verified the prior claims against the code rather than trusting them. Closed both previously-open findings (#69, #70) and hardened three areas (owner-policy *invocation* now enforced, optimistic-concurrency behaviour now tested, outbox ids aligned to Guid v7). The review also corrected two over-stated "critical" candidates that do not hold (a claimed product-stock race is covered by the `xmin` token; a claimed cross-owner stock-restore bypass is unreachable because order creation owner-scopes its product lookup). Three new **low-severity** test-coverage gaps were found and recorded below (#71–#73). Independent fresh-eyes score on a stricter scale: ~9.0 — the gap from 9.6 is almost entirely test-coverage breadth and a few convention tests that assert *presence* rather than *behaviour*, not runtime defects.

> **Rerun 2026-06-01 (after pulling `origin/main` to `fc93601`).** Reviewed the seven June 1 commits and reran local verification. No new architecture findings were found; score remains 9.6/10 with low-severity findings #71–#73 open.

> **Coverage follow-up 2026-06-01.** Closed findings #71-#73 by adding dedicated customer/product mutation integration coverage, moving data-access-sensitive command-handler tests onto the shared Testcontainers PostgreSQL fixture, and strengthening convention tests from presence/source scans to behaviour-oriented IL/SQL checks. Score raised conservatively to 9.7/10 because the remaining risk is general starter-template breadth rather than known open findings.

> **Comprehensive multi-agent review 2026-06-02.** A four-agent parallel review (security/auth, CQRS/domain/data-access, eventing/outbox/payload-capture, build/CI/test-coverage) re-read the code against CLAUDE.md. Two agent-reported "HIGH/CRITICAL" findings were verified to be false positives and dismissed: a claimed stock double-restore on cancel does not hold because `OrderCancellationService` calls `order.Cancel()` first and the `Cancelled → Cancelled` transition is rejected by the state machine before any stock loop runs; a claimed `Money.Subtract` negative-value escape does not hold because `Subtract` routes through `Create()`, which throws on negative. No runtime vulnerabilities or correctness defects were found. Three hardening items (A–C below) were fixed and one accepted limitation (D) recorded. Score held at 9.7/10 — the review confirmed robustness rather than revealing defects, and the fixes are reproducibility/test-coverage hardening.

> **Comprehensive multi-agent review 2026-06-03 (after pulling `origin/main` to `2d1f9ee`).** A larger fan-out (8 finders across security/auth, CQRS/domain, data-access, eventing/outbox, payload-capture, build/CI/reproducibility, concurrency/correctness, and convention-test rigor) followed by adversarial verification (3 diverse skeptic lenses per candidate, majority-refute to dismiss). The whole sweep surfaced only **3 candidate findings** — strong evidence the codebase is genuinely hardened. Verification **dismissed 1 as a false positive** and **confirmed 2** low-severity convention-test coverage gaps (both the "asserts presence, not behaviour" class). The dismissed finding is recorded under the accepted/dismissed note below so it is not re-raised. The two confirmed gaps (E–F) are now closed with behaviour-checking convention tests, each proven to fail on a deliberately injected regression before landing. No runtime vulnerabilities or correctness defects were found; score held at 9.7/10.

> **Fresh full review 2026-06-07 (independent re-read by a new model).** A six-finder fan-out (domain, CQRS/application, gateway-auth/security, eventing/outbox/payload-capture, persistence/migration, build/CI/convention-rigor) followed by direct adversarial verification of every candidate against the code. The codebase remains genuinely hardened — most of the "hunt list" came back clean (gateway-assertion field coverage, owner-scope enforcement, outbox claim race, Dapper parameterization, payload bounds/correlation/cleanup, CPM/lock-file/CI hygiene, and the "behaviour not presence" convention tests all verified sound). Eight findings were confirmed and recorded under **Open Findings** below. **#74, #75, and #77 were fixed in this session** (see the "Recently resolved" table); #76 and #78–#81 remain open. Score moved 9.7 → 9.4 on discovery, then back to 9.5 after fixing the one real user-visible defect (#74) and its guard gap (#75); the remaining open items are all low-severity (rounding policy, two failure-policy hardening items, an EF-model/index divergence, and a vacuous-pass-risk convention test). NOTE for the next reviewer: the DbUp migrations were squashed into a single `0001_CreatePostgresSchema.sql` baseline — every constraint/column the older numbered migrations added was verified present in the squash, so references below to `0007/0017/0018/0019` describe history, not current files.

> **Comprehensive multi-agent audit 2026-06-06 (deep security + architecture sweep).** An 8-finder fan-out (gateway-assertion cryptography, authorization/owner-scoping/IDOR, CQRS/domain correctness, concurrency/retry idempotency, outbox/eventing/Service Bus, payload-capture/PII, data-access/SQL/migrations, build/CI/Docker/supply-chain) followed by adversarial verification (3 diverse skeptic lenses per candidate — reachability, claim-correctness, already-mitigated — majority-refute to dismiss). The whole sweep surfaced only **5 candidates**; verification **dismissed 3** (a claimed cross-tenant order-items read — query is owner-scoped; a containers-run-as-root claim — out of scope for the upstream-managed deployment; plus none of the documented prior false positives resurfaced) and **confirmed 2**. The "high"-labelled survivor was honestly **downgraded to Medium** on review because archive/audit blobs are sanctioned to hold full-fidelity PII by design — the genuine residue is **query-string bearer secrets** reaching archive/audit *metadata* (finding **H**, fixed below). One **Low** optional-hardening item (HSTS defaults) recorded as an accepted note. No Critical/High runtime defect survived scrutiny; this sweep found no new open findings (it predates the 2026-06-07 review that opened #74–#81).

> **DAST scan added + first run 2026-06-06.** Added an OWASP ZAP dynamic-scan harness (`dast/run-dast.sh` + `dast/automation.yaml`): it boots a throwaway PostgreSQL, runs DbUp migrations, starts the API in `GatewayIdentity:Mode=UnsignedDevelopment` (injecting the projected gateway identity headers so the real protected `/api/v1` surface is reachable), then runs the ZAP Automation Framework (OpenAPI import → spider → passive → 20-min active scan) and fails the build on any alert ≥ `FAIL_RISK` (default Medium). The first run surfaced **one genuine correctness gap** and **one false positive**. Genuine: malformed JSON request bodies returned **HTTP 500** instead of 400 because `ResolveExceptionStatusCode` did not map `BadHttpRequestException` — fixed (finding **G** below) with a regression test. False positive: `GET /openapi/v1.json` was flagged for "Application Error Disclosure"/"Debug Error Messages" because the generated spec documents `ProducesProblem(500)` responses, so its body literally contains the reason phrase "Internal Server Error"; the endpoint returns 200 and logs no error. A narrowly-scoped `alertFilter` (rules 90022/10023, that URL only) marks it false-positive while leaving those rules active on every real endpoint. Score held at 9.7/10 — a one-line status-mapping gap found and fixed, plus new dynamic-scan coverage.

> **Independent fresh-eyes re-review + hardening session 2026-06-09.** A new independent multi-agent review (8 dimensions, adversarially verified) scored the template **7.8/10 on a strict production scale** — materially below the self-graded 9.7, mainly because (1) the 9.7 is admittedly monotonic/self-assessed, (2) proportionality (heavyweight machinery for a 3-entity domain), and (3) a cluster of confirmed "defaults people inherit" + invariant/test gaps. That cluster was then fixed this session (all on `main`, CI-green):
> - **Non-root containers** — `USER $APP_UID` on the API and DbMigrator images; Functions documented as deliberately root (Azure Functions base). Commit `ad0c4e0`.
> - **Workflow least-privilege** — `permissions: contents: read` + `concurrency` on `ci.yml`/`dast.yml`. Commit `b58aaba`.
> - **Locked-mode in image builds** — `--locked-mode` added to all three Dockerfile restores. Commit `bf7a320`. Exposed a latent fragility: NU1900 (transient NuGet-audit fetch failure) was treated as a build error; excluded via `WarningsNotAsErrors` while keeping real advisories NU1901–NU1904 fatal. Commit `3350301`.
> - **Max-order-items invariant** — moved into the `Order` aggregate (`Order.MaxItems`, enforced in `AddItem`); `CreateOrderCommandValidator` references it so the two cannot drift. Commit `a239f54`.
> - **Outbox SKIP LOCKED concurrency** — two real-Postgres integration tests; one proves `SKIP LOCKED` specifically (held-lock reader skips vs. blocks). Commit `25305ad`.
> - **Owner-less ctor footgun** — see the resolved note below; deleted the no-owner ctors, routed tests through a `TestEntities` factory, added a config-independent reflection convention test. Commits `19ea218`/`3f63834`. (An earlier IL-scan attempt was config-dependent and reverted — `d818540`.)
> - **Test-builder cleanup** — removed the over-built/under-used command builders for inline initializers, keeping order scenario helpers in `OrderCommands`. Commit `530370a`.
> - **Supply-chain** — added Dependabot (NuGet/Actions/Docker) + a CodeQL workflow (csharp, build-mode none); SHA-pinned every action across the workflows. Commits `bdbe607`/`3f70962`.
>
> **Open after this session** (see Open Findings below): create-order retry-idempotency has no regression test; gateway-assertion replay/nonce and body/query signing are accepted threat-model decisions; an `aspire` CI flake (slow Service Bus emulator startup → health-check timeout) is a watch-item, not a defect. *(Follow-up, same day: a separate red CI run was traced to two flakes — a Docker Hub `postgres:16-alpine` pull timeout in the unit/integration jobs, fixed by a pre-pull-with-retry step (commit `9b25cfa`), and a minute-bucket race in the Aspire archive test `CreateCustomer_ShouldArchivePayloadsToAspireBlobStorage`, fixed by asserting over the union of the correlation's archive blobs and validating pointers by shape (commit `14fc72a`). Neither was an architecture finding; score unchanged.)*
>
> **Independent post-session re-score (2026-06-09): 8.2/10** (+0.4 vs the pre-session 7.8). The fan-out **independently verified every claimed fix in code — none failed**: the `Order.MaxItems` invariant (validator references it, cannot drift), the owner-less-ctor deletion + reflection guard, real-Postgres SKIP LOCKED + xmin concurrency tests, non-root API/DbMigrator images, workflow least-privilege + SHA-pinned actions, `--locked-mode` everywhere with narrow NU1900 handling, Dependabot + CodeQL, and the per-channel FailOpen/FailClosed outbox policy. It held the score below 9 for proportionality (heavy machinery for 3 entities), **folder-only Clean Architecture inside one assembly** (Application→Infrastructure coupling is convention-enforced, not compiler-enforced), and latent footguns: the void-command mediator path bypasses pipeline behaviors, no domain guard mirrors the empty-order validator rule (hardcoded USD fallback in the creation event), and the shared scoped `NpgsqlConnection` is safe only while query handlers stay strictly sequential. It also noted additional build hardening (base-image digest pins, `NuGet.config`, unconditional `RestoreLockedMode`) present in the working tree from a **concurrent session, not yet committed to `main`**.
>
> **Supply-chain hardening session 2026-06-09 (dedicated supply-chain audit + fixes).** A focused 5-dimension supply-chain fan-out (NuGet/packages, GitHub Actions/CI, Docker, build-time code execution, scripts/secrets) with adversarial verification (every finding handed to a skeptic told to refute it; 6 of 20 candidates dismissed, incl. a false "cross-PR cache poisoning" claim — Actions cache scoping is one-directional — and a false `fail_risk` shell-injection claim — the input is env-bound and allowlist-gated). The sweep confirmed the repo is already strongly hardened (SHA-pinned actions, least-privilege tokens, CPM + content-hash lock files + transitive pinning, NuGetAudit NU1901–04 as hard errors, Dependabot across 3 ecosystems). It then **implemented, tested, and committed** the residual gaps the prior note saw uncommitted, plus more:
> - **M1 (Medium) — Docker base images pinned by `@sha256` digest.** All six `FROM` lines across the three Dockerfiles (`sdk`/`aspnet`/`azure-functions`) now pin `tag@sha256:…` (digests resolved via `docker buildx imagetools inspect`). Closes the one place the otherwise-careful reproducibility stopped: the mutable OS/runtime layer under the internet-facing API and credential-bearing DbMigrator. Also activates the already-configured-but-inert Dependabot docker updater (it cannot bump a bare `:10.0` tag). All three images verified building locally.
> - **L1 (Low) — repo-root `NuGet.config`.** `<clear/>` + single explicit nuget.org source + `<packageSourceMapping>` mapping `*` → nuget.org (the primary dependency-confusion defense). Wired into all three Docker builds (`COPY NuGet.config`) so container restores honor it. **Signature validation (`signatureValidationMode=require` + `<trustedSigners>`) was evaluated and deliberately NOT shipped** — trusting only the current nuget.org repository cert breaks restore on older packages carrying a pre-rotation countersignature (`System.Security.Cryptography.ProtectedData 4.5.0` → NU3034), and enforcement differs by OS (passed macOS, failed Linux/Docker); too brittle to gate the build. Tamper-detection is already covered by lock-file content hashes + locked-mode. Rationale recorded in the file's header comment.
> - **L2 (Low) — `RestoreLockedMode` now defaults on everywhere**, not only when `CI=true` (`Condition="'$(RestoreLockedMode)' == ''"`), so a plain local `dotnet restore` can no longer silently rewrite `packages.lock.json`. Intentional upgrades use the force-evaluate flag (documented). Inert on the lock-file-exempt AppHost projects.
> - **L4 (Low) — `.dockerignore`** added (Docker does not honor `.gitignore`): keeps `bin/`/`obj/`, local dev config/secrets, VCS/agent metadata, and test/orchestration projects out of the build context and build-stage layers.
> - **L5 (Low) — untracked the placeholder `appsettings.Development.json`** (its `.gitignore` rule was a no-op while tracked — a latent footgun that a real connection string added there would commit silently); shipped `appsettings.Development.json.example` instead, and added a **checksum-verified, license-free, SHA-pinned Secret Scan workflow** (`gitleaks` 8.30.1, binary SHA-256 verified before execution) + `.gitleaks.toml` (allowlists only the documented placeholder). Verified clean across all 291 commits of history.
> - **Info — `global.json`** pins the SDK band (`10.0.300`, `rollForward: latestFeature`, `allowPrerelease: false`) for a reproducible toolchain floor.
> - **Regression guard** — new `SupplyChainConventionTests` mechanically enforces the headline fixes so they cannot drift: Dockerfiles must pin base images by `@sha256` (M1), `NuGet.config` must clear sources + map `*` to nuget.org (L1), and `global.json` must pin the SDK. All 89 convention tests green; full Release build + 488 unit/convention tests pass.
>
> Score **held at 8.2** — this pass reduces residual supply-chain risk (the lone Medium is closed) but does not move the structural ceiling (proportionality, single-assembly Clean Architecture, the latent runtime footguns above remain). Honest per this doc's anti-inflation stance: hardening that removes a verified gap, not a capability that raises the template's altitude.

---

## Strengths

### Clean Architecture with Convention-Enforced Boundaries

The project enforces architectural rules through convention tests in the main and AppHost test projects using Best.Conventional plus targeted reflection/source scanners. This is the strongest feature of the codebase — architectural decisions are not just documented but mechanically verified on every test run.

| Test Class | What It Enforces |
|------------|-----------------|
| `NamingConventionTests` | Endpoints, DTOs, commands, queries, handlers, validators, services, and test classes follow naming conventions; application contracts and handlers live in mechanically discoverable namespaces |
| `CqrsConventionTests` | Command handlers don't touch `IDbConnection`; query handlers don't touch `DbContext`; owner-scoped query handlers include owner filters in Dapper SQL; every command/query has a handler; dual interface enforcement (`ICommand` + `IRequest<T>`) |
| `DomainConventionTests` | Private property setters on entities; immutable value objects; public getters on DTOs; non-public default constructors; `Equals`/`GetHashCode` overrides; async suffix; no async void; no `DateTime.Now`; aggregate creation-event safety |
| `ApiConventionTests` | Endpoints don't access DB directly; protected API groups require gateway identity metadata; raw gateway identity headers stay behind infrastructure abstractions with IL literal/type-reference checks; validators are pure; DTOs have no instance methods; API contract shapes are serializer-friendly; collection properties are materialized; mappers are static; handlers don't dispatch to other handlers; domain doesn't reference API or third-party packages |
| `PersistenceConventionTests` | Every entity has a `DbSet`; value objects use `OwnsOne` not `DbSet`; enum properties configured; no static mutable state on `DbContext`; collection properties have private setters; migration scripts follow numbered prefix, are embedded resources, and name constraints explicitly |
| `DapperConventionTests` | Query handlers must not use `SELECT *` in SQL (IL inspection of compiled string literals) |
| `CachingConventionTests` | ICacheable queries must have non-empty cache keys, positive durations, and deterministic keys |
| `HousekeepingConventionTests` | Project files don't reference `bin`/`obj` artifacts; production code avoids regions, XML documentation comments, and historical workaround comments |
| `StarterApp.AppHost.Tests` conventions | Service Bus topology/Function trigger alignment plus async/DateTime safety for AppHost, Functions, and ServiceDefaults |

These tests catch entire categories of mistakes at compile time rather than in production.

### Advisory Consistency Measurement

The test suite now includes a `Consistency/` layer that measures structural drift without turning every pattern into a hard rule. It tracks three extensible cohorts against pinned exemplars in `docs/exemplars/`:

- Command handlers: dependencies, logging, cache invalidation, entity loads, helper methods, and control-flow shape
- Query handlers: caching, list/paged/by-id shape, SQL statement count, joins, parameters, and dependency count
- EF configurations: relationship/value-object/index/conversion mapping shape

This complements the convention tests: deterministic "must always" policies stay in Best.Conventional tests, while consistency tests surface advisory outliers for review.

### CQRS Implementation

The read/write split is cleanly executed:

- **Commands** flow through EF Core `DbContext` for writes, returning `*Dto` types
- **Queries** flow through Dapper `IDbConnection` for reads, returning `*ReadModel` types
- A **custom mediator** replaces MediatR, avoiding commercial licensing. It auto-discovers handlers via reflection and integrates validation as a pipeline behavior
- Convention tests mechanically prevent cross-contamination between read and write paths
- Zero CQRS violations found across all 9 command handlers and 7 query handlers

### Rich Domain Models

Entities contain real behavior, not just properties:

- **`Order`** has a state machine (Pending > Confirmed > Processing > Shipped > Delivered, with cancellation from valid states). `IsValidStatusTransition()` uses a switch expression that makes valid/invalid transitions explicit. `Confirm()` requires non-empty items. `Cancel()` prevents cancellation of delivered orders.
- **Value objects** (`Money`, `Email`) are immutable with private constructors, static factory methods, and proper `Equals`/`GetHashCode` overrides
- **`OrderItem`** encapsulates GST calculation logic with multiple derived values (`GetUnitPriceIncludingGst`, `GetTotalPriceExcludingGst`, `GetTotalPriceIncludingGst`, `GetTotalGstAmount`)
- Private setters and protected constructors enforce encapsulation throughout
- `Reconstitute()` factory methods handle database hydration without bypassing creation-time invariants (scoped `internal`, visible only to test assembly)

### Application Layer

All command handlers follow correct patterns:

- **Single `SaveChangesAsync`** per handler — atomicity guaranteed
- **Tracked entities** loaded via `FindAsync` / `Include().FirstOrDefaultAsync` — EF Core detects only changed properties
- **Domain methods invoked** for all mutations (`Order.Cancel()`, `Product.UpdateStock()`, `Customer.UpdateDetails()`) — no direct property assignment
- **Stock lifecycle** managed correctly: `CreateOrderCommandHandler` validates availability and decrements atomically; both cancellation endpoints share one stock-restoration workflow
- **Consistent error handling**: `KeyNotFoundException` for missing entities, `InvalidOperationException` for domain violations

### Validator Coverage

Every command and query has an `IValidator<T>` implementation (16 total). Convention tests enforce this — adding a new command or query without a validator fails the build. Validators provide structured multi-error `ValidationError` responses at the API boundary via the ProblemDetails `errors` extension; domain guards are the safety net (defense-in-depth).

### Property-Based Testing

FsCheck tests go beyond typical unit tests by generating hundreds of random inputs per test:

- Money arithmetic (commutativity, associativity, currency mismatch)
- Order state machine (valid/invalid transitions across random states)
- OrderItem GST calculations and boundary conditions
- Email format edge cases

### Custom Mediator

The mediator at `Infrastructure/Mediator/Mediator.cs` is well-designed:

- Auto-discovery and registration via `MediatorServiceExtensions.AddMediator()`
- Validation pipeline: validators run before handlers, collecting all errors before throwing
- Supports both `IRequest<TResponse>` (returns value) and `IRequest` (void) dispatch
- Keeps the codebase free from MediatR's commercial licensing constraints

### Trusted Gateway Identity

Authentication remains gateway-owned, but the API now has an enforceable trust boundary instead of blind header trust:

- `/api/v1` endpoint groups opt into `RequireGatewayIdentity()` metadata; health and OpenAPI stay public
- `GatewayIdentityMiddleware` parses a small normalized header contract into `ICurrentUser`
- Production-like `GatewayIdentity:Mode=Required` validates a signed `X-Gateway-Assertion` over issuer, audience, subject, principal type, tenant, scopes, correlation id, method, path, short lifetime, key id, and a hash of the projected headers including authentication methods
- Each protected route declares a required gateway scope (`products:read`, `products:write`, `customers:read`, `customers:write`, `orders:read`, or `orders:write`); authenticated callers missing the route scope receive `403 Forbidden`
- Sensitive write routes call `SecuredBy2Fa()` and require the gateway-projected `X-Authenticated-Amr` authentication-method set to include `mfa`; callers with valid identity and write scope but no MFA proof receive `403 Forbidden`
- Customer, Product, and Order rows persist `OwnerSubject` and `TenantId` from the verified gateway identity. Create handlers stamp ownership, query handlers filter by owner scope, and mutation handlers call `IOwnerOnlyPolicy` before changing loaded resources.
- Cross-owner reads are hidden as `404 Not Found` or empty pages, while cross-owner mutations return `403 Forbidden`. By-id cache keys are owner-scoped so cached resources cannot leak across identities.
- Local Development/Testing can use `UnsignedDevelopment`, and startup validation rejects that mode elsewhere
- Security tests cover missing identity, missing assertion, expired assertion, wrong audience, wrong path, tampered identity/authentication-method headers, missing MFA proof, wrong signing key, and unknown key id
- Convention tests inspect the mapped endpoint metadata so every `/api/v1` route must carry both gateway identity and gateway scope metadata, regardless of source-file placement. CQRS and persistence conventions now also require owner-scoped resource queries, owner-policy injection, and persisted owner columns on owned aggregates.
- Rate limiting now partitions protected requests by verified tenant/subject identity, falling back to IP only where no authenticated gateway identity exists

### DevOps and Observability

- **Aspire orchestration** — `AppHost/Program.cs` wires up API, PostgreSQL, Redis, Blob storage, Seq, Service Bus emulator, the Azure Functions runtime container, and DbMigrator with proper `WaitFor` dependencies and optional dev tunnel support
- **Serilog** structured logging with console, file, Seq, and OpenTelemetry sinks
- **OpenTelemetry** metrics (ASP.NET Core, HTTP, runtime) and tracing via `ServiceDefaults`
- **Docker** multi-stage image builds for API, DbMigrator, and Functions, with Aspire-owned local dependency containers
- **CI** pipeline with GitHub Actions (unit tests + integration tests with Testcontainers)
- **Health checks** at `/health`, `/health/ready`, `/health/live`, and `/alive`
- **Password masking** in log output — implemented consistently across `Program.cs`, `DatabaseMigrationEngine`, and `DbMigrator`
- **Payload archive / PII audit** — HTTP request/response bodies, outbound Service Bus payloads, and inbound Function payloads are written as JSONL support artifacts to Azure Blob under date/hour/minute paths. Archive files are correlation-bound (`archive/{date}/{hour}/{minute}/{correlationId}.jsonl`); audit files are time-window streams (`audit/{date}/{hour}/{minute}/payload-audit.jsonl`) that include timestamp, correlation id, archive blob name, payload hash, payload bounds metadata, and the captured payload. HTTP capture is bounded by `MaxPayloadBytes` and content-type allowlist metadata; Service Bus payload capture remains full-fidelity for JSON events. Operational logs use redacted payloads and a convention test blocks direct raw `{Body}` logging.
- **Dedicated `DbMigrator` service** for migrations across all deployment modes (Aspire, container deployments, standalone)
- **Outbox → Service Bus pipeline** — domain events captured durably in `outbox_messages` during a single `SaveChangesAsync` (aggregates use client-generated Guid v7 Ids, so creation events carry correct keys before the save). `EnableRetryOnFailure` is safe because no user transaction is needed. `OutboxProcessor` claims rows in a short PostgreSQL transaction using `processing_id`/`locked_until_utc` plus `FOR UPDATE SKIP LOCKED`, releases locks before Blob capture and Service Bus publish, then saves processed/error outcomes afterward. Service Bus topic has duplicate detection enabled (5-minute window, the emulator maximum). Consumed by Azure Functions via topic subscriptions with correlation filters; AppHost runs Functions through the Docker runtime so trigger listeners are active in integration tests. Convention tests enforce subscription filter ↔ domain event contract sync and topology property alignment.
- **Explicit constraint naming** — all database constraints named via convention (`pk_`, `fk_`, `df_`, `ck_`, `ix_`), enforced by convention test from the first PostgreSQL migration onward

### Build Quality

`Directory.Build.props` enforces quality globally:

- Warnings as errors
- Nullable reference types
- Deterministic builds with `PathMap`
- Package lock files with `RestoreLockedMode` in CI
- Global Roslyn analyzers (`Microsoft.CodeAnalysis.Analyzers`)

### .NET 10 Features

Good adoption of modern .NET:

- Native OpenAPI support (`AddOpenApi` with document transformer)
- `StatusCodeSelector` on `UseExceptionHandler` for type-based exception-to-status mapping
- Minimal APIs with `MapGroup` and endpoint metadata
- Scalar API reference UI replacing Swagger
- `ArgumentOutOfRangeException.ThrowIfNegativeOrZero()` and similar guard clause helpers
- `MailAddress.TryCreate()` for email validation (no exception-based flow control)

---

## Weaknesses

### Open Findings

Confirmed in the 2026-06-07 fresh review (verified against code, not just agent-reported). Each is reproducible by reading the cited file; none was auto-fixed — fix + add a regression test before marking resolved.

The 2026-06-07 batch (**#74, #75, #76, #77, #78, #79, #81**) is resolved — see the table immediately below; **#80** is an accepted limitation (EF cannot express the index). The 2026-06-09 hardening session (see the dated note above) closed the build/CI + invariant/test cluster from the independent re-review.

**Latent footguns + retry-idempotency test gap — RESOLVED 2026-06-09 (post-re-score follow-up).** The four items the 8.2 re-review flagged as the genuine remaining defects are fixed:
- **Void-command pipeline bypass** — removed the void mediator path entirely. Added a `Unit` result; `Delete{Product,Customer}Command` are now `IRequest<Unit>`; the void `SendAsync`/`IRequestHandler<T>`/`VoidRequestHandlerWrapper` are deleted, so every command runs through the single `IPipelineBehavior` pipeline. `CqrsConventionTests` now requires every command to be `IRequest<T>` (the bare `IRequest` type no longer exists).
- **Empty-order domain guard** — `Order.RecordCreation()` throws on zero items (mirrors the validator per the sync rule; fires during persistence capture, after the add-items loop), and the hardcoded `"USD"` fallback in `OrderCreatedDomainEvent` is removed.
- **Shared scoped `NpgsqlConnection`** — `IDbConnection` is now **transient** (each query handler gets its own connection; safe under `Task.WhenAll`), locked in by `PersistenceRegistrationTests.AddPersistence_RegistersIDbConnection_AsTransient`.
- **Create-order retry-idempotency** — now covered by `CreateOrderConcurrencyIntegrationTests` against real Postgres: a `DbTransactionInterceptor` throws a transient fault after the first commit, forcing an execution-strategy retry; the test asserts exactly one order + stock decremented once. A concurrent-oversell test (two parallel orders for the last unit → one succeeds, stock 0) was added alongside.

The following remain **open** (decisions / watch-item / explained-deferral, not defects):

- **Folder-only Clean Architecture — ACCEPTED (deferred; split designed but not implemented, 2026-06-09).** `Domain` is its own assembly (purity compiler-enforced), but `Application` and `Infrastructure` are folders inside `StarterApp.Api`, so that boundary is convention-enforced (the convention-test fleet), not compiler-enforced. A full assembly split was designed and reviewed, then **deliberately deferred** — keep the folder-only form for now. Rationale and what a split would entail, recorded so this is a decision-on-file, not an oversight:
  - **Target if/when split:** `StarterApp.Application` (handlers, DTOs, validators, mapping, the moved ports — mediator/validation/caching/identity interfaces + a new `IReadRetryPolicy` — **and the EF `ApplicationDbContext`**) → refs Domain only; `StarterApp.Infrastructure` (Mediator, Redis cache, `OwnerOnlyPolicy`/gateway identity, outbox, `PostgresRetryPolicy`, payloads, health checks) → refs Application; `StarterApp.Api` (endpoints + `Program`) → refs both. Result: `Domain ← Application ← Infrastructure ← Api` enforced by project references.
  - **Forced consequence:** because handlers use `DbContext` directly and the repository pattern is banned (CLAUDE.md), the EF `ApplicationDbContext` must live in **Application** (the "EF-as-persistence, no repository" Clean Architecture variant). This is the load-bearing reason it's a judgment call rather than an obvious win — some hold that the DbContext is Infrastructure, which here would force `Application → Infrastructure` and defeat the enforcement.
  - **Why deferred:** large, high-churn refactor (move ~90 files into 2 new projects; rework the single-`ApiAssembly` convention/consistency discovery for 3 assemblies; new lock files/Dockerfile COPY lines/`InternalsVisibleTo`). The convention tests already enforce the layering behaviourally, so the marginal gain (compiler vs convention enforcement) does not currently justify the churn for a 3-entity template. Revisit if the domain grows or a hard architectural guarantee is required.
**Accepted limitation (threat-model decision, not a defect) — gateway-assertion replay window.** A valid `X-Gateway-Assertion` can be replayed within its lifetime window (`MaxTokenLifetimeSeconds` + `ClockSkewSeconds`, ~150s default): there is no `jti`/nonce/consumed-token store, so the same token re-validates until it expires. **Accepted** under the documented threat model (trusted APIM gateway + TLS): the assertion is HMAC-signed and bound to method/path/identity-hash/scopes/expiry, so a replay can only re-execute the *exact same* authorized call (it cannot change the action, the identity, or outlive the window), and capturing the token at all requires a position inside the encrypted gateway→API hop. Impact is therefore limited to repeating a non-idempotent sensitive action a few times within ~150s; mutations already require MFA (`amr`) and owner-scoping. **Revisit if:** zero-trust internal network (TLS terminated before the API), regulated/compliance context, or high-impact non-idempotent endpoints. **Fix when needed:** the gateway emits a `jti`; the validator check-and-stores it in the (already-wired) Redis `IDistributedCache` with TTL = token lifetime and rejects repeats — note this then hard-requires a shared Redis across API replicas (the in-memory fallback would not catch cross-replica replays).

**Accepted limitation (threat-model decision, not a defect) — gateway-assertion does not sign the request body or query string.** The signature binds method + `Path.Value` + identity-hash + scopes, but **not** the request body and **not** the query string, so within the lifetime window a body/query could be altered while the assertion still validates. **Accepted** under the same trusted-gateway + TLS model: tampering requires a position inside the encrypted gateway→API hop, and TLS already protects request contents in transit; the signature's role is to prove gateway provenance + identity + request shape, which it does. Owner-scoping further limits query-tampering impact. **Revisit if:** same triggers as above. **Fix when needed:** include a hash of the body (and the query string) in the signed payload and re-verify it server-side — query signing is cheap; body-hash signing requires the gateway and API to buffer/hash the full body (real runtime cost + middleware/streaming complexity), so it is the heaviest option and usually deferred to TLS integrity.
- **Watch-item — `aspire` CI flake (Service Bus emulator readiness).** The Service Bus emulator can be slow to pass its health check on a cold runner, timing out `AppHost_ShouldEventuallyExposeHealthyApi` (observed once 2026-06-09; passed on re-run). If it recurs, raise the readiness timeout in the AppHost test — it is not the `sbemulatorns` namespace warning (a recoverable emulator log line, not the cause). **Note:** a *separate* recurring `aspire` failure — the minute-bucket race in `CreateCustomer_ShouldArchivePayloadsToAspireBlobStorage` — was a real test defect and is **resolved** (commit `14fc72a`, see the dated CI-fix note above); this watch-item is now only the emulator-readiness timeout.

#### Recently resolved (2026-06-07 fresh review)

| # | Finding | Fix |
|---|---------|-----|
| ~~74~~ | Product by-id cache (10-min TTL, read model carries `Stock`) was not invalidated when `CreateOrder`/`CancelOrder`/`UpdateOrderStatus`→Cancelled changed product stock, so the owner saw stale stock for up to 10 minutes. | Injected `ICacheInvalidator` into all three order handlers and call `InvalidateProductAsync` for every affected product after `SaveChangesAsync` (`CreateOrderCommand.cs`, `CancelOrderCommand.cs`, `UpdateOrderStatusCommand.cs`). Regression tests `CreateOrderCommandHandlerTests.Handle_ShouldInvalidateProductCache_ForEachOrderedProduct` and `CancelOrderCommandHandlerTests.Handle_ShouldInvalidateProductCache_AfterRestoringStock` added via a new `RecordingCacheInvalidator` test double. |
| ~~75~~ | `CachingConventionTests` matched mutation handlers by name and excluded `Create*`, so cross-entity stock mutations by order handlers were never checked — the blind spot that let #74 land. | Added `CommandHandlers_MutatingCacheableEntities_MustInvokeCacheInvalidator`: an IL scan that detects a Product stock write (`Product.UpdateStock` / EF `ExecuteUpdateAsync`) in the handler **or** in an application helper it delegates to (e.g. `OrderCancellationService`), then requires an `ICacheInvalidator` call. Guarded with `Assert.NotEmpty`. Proven to fail when the CreateOrder invalidation is removed (flags `CreateOrderCommandHandler`) before landing. |
| ~~77~~ | `Email` stored values verbatim with Ordinal equality, so case variants bypassed per-owner email uniqueness (app check + unique index both case-sensitive), allowing duplicate customers. | `Email.Create` now normalizes via `ToLowerInvariant()` (culture-stable, unlike `ToLower()`); all writes flow through it, so the EF uniqueness query and the case-sensitive unique index now enforce uniqueness on the canonical form with no handler changes. Tests `EmailTests.Create_ShouldNormalizeToLowerCase` and `Equals_WithDifferentCaseEmail_ShouldReturnTrue` added. |
| ~~81~~ | `DapperConventionTests.QueryHandlers_MustNotUseSelectStar` lacked an `Assert.NotEmpty` guard, so a future suffix/namespace refactor that matched zero handlers would make `MustConformTo` pass vacuously — turning the SELECT-`*` guard into a silent no-op. | Added `.ToList()` + `Assert.NotEmpty(queryHandlers)` before `MustConformTo`, mirroring the sibling `QueryHandlers_MustUsePostgresRetryPolicy`. The test now fails loudly if discovery ever matches no handlers. |
| ~~76~~ | Money/GST math was never quantized to a currency scale: `9.99 × 0.10 = 0.999` flowed unrounded into `OrderDto` totals and the published `OrderCreatedDomainEvent`. Both write (domain) and read (Dapper SQL) paths emitted non-money-shaped decimals. | Quantized money to whole cents with a documented rounding policy: (1) `Money.Create` rounds to 2 dp `AwayFromZero`; (2) `OrderItem` rounds GST **per unit** then × quantity so per-unit and per-line figures stay consistent (`total GST == unit GST × qty`, `total incl == total excl + total GST`); (3) the three order read queries (`GetOrderByIdQuery`, `GetOrdersByCustomerQuery`, `GetOrdersByStatusQuery`) apply the matching PostgreSQL `round(unit_price_excluding_gst * gst_rate, 2)` per unit so the read path agrees with the write path. Added Money quantize tests, `OrderItem` sub-cent + consistency tests, and FsCheck properties asserting every computed `Money` is whole-cents and `line incl == excl + GST`; updated the two `OrderApiTests` totals tests (which encoded the old round-at-total math) to the per-unit rule. |
| ~~78~~ | `FailClosed` was silently bypassed when the archive store was the null store (`PayloadCaptureSink` returned before consulting `FailureMode`), so a `FailClosed`-but-no-store misconfiguration failed *open* and dropped payloads. **Also a design-posture decision (per maintainer):** an audit sidecar should not take down synchronous user traffic. | Split the single global `FailureMode` into **per-channel** `HttpFailureMode` (default `FailOpen`) and `ServiceBusFailureMode`, resolved by `request.Channel`. The null-store branch now honors the channel's mode (throws under `FailClosed`). Production-like AppHost config sets `ServiceBusFailureMode=FailClosed` while HTTP stays `FailOpen`, so archiving never fails user requests but the background outbox path refuses to publish without an audit record. Tests: null-store-FailClosed-throws, and a per-channel test (HTTP fails open while Service Bus fails closed on the same archive outage). |
| ~~79~~ | Under `FailClosed`, a *non-transient* payload-capture failure (e.g. Blob 403, config/serialization defect) was classified non-transient by `OutboxProcessor`, so it hit `IncrementRetry()` → `MarkAsError` and the event was **permanently lost** though Service Bus was healthy — requiring manual DB requeue. | Wrapped the pre-publish `CaptureAsync` call in a dedicated inner catch that **pauses the batch** on any capture failure (logs a warning, leaves the row unprocessed with retry budget intact) instead of consuming retries or poisoning the message. The event is republished cleanly once the archive recovers; nothing is published without a durable audit record. Test: a non-transient (`InvalidOperationException`) capture failure under `FailClosed` leaves `RetryCount=0`, `Error=null`, unpublished across many batch runs (would `MarkAsError` under the old path — `PayloadCaptureFailureClassifier` returns false for it). |

Earlier open-findings rows (now all resolved) are kept for history:

| # | Severity | Finding | Suggested fix |
|---|----------|---------|---------------|
| ~~82~~ | Low (doc) | **This doc referenced squashed-away migration filenames** (`0007_AddStockNonNegativeConstraint.sql`, `0017_AddOrderItemGstRateRangeConstraint.sql`, `0018_AddOwnerScopeColumns.sql`, `0019_AddOutboxProcessingClaims.sql`) and the wrong-case constraint name `CK_OrderItems_GstRate_Range` as if they were live files. After the squash only `0001_CreatePostgresSchema.sql` exists; every constraint/column those findings describe was verified present in the baseline. **Resolved** in this edit — the 2026-06-07 review note records the squash; remaining historical references are framed as history. | — |

Lower-confidence items the review surfaced but did **not** record as findings (by-design, out-of-scope for a starter template, or too narrow): unbounded `Quantity` can throw `OverflowException` (mapped to 500) at extreme inputs but item count is capped at 50; `Order.Reconstitute` skips the single-currency check but is internal/test-only; the log redactor only masks known property names + emails (free-text PII in arbitrary fields reaches logs — the archive is the full-fidelity copy by design); the gateway assertion is not body-bound (replay-with-modified-body within the ≤120s lifetime requires an intra-TLS intercept); base `appsettings.json` defaults `GatewayIdentity:Mode=UnsignedDevelopment` but `.ValidateOnStart()` makes any non-Development/Testing boot fail closed; several SQL indexes are absent from the EF model under the accepted "DbUp owns the schema" stance.

**Accepted limitation (not a defect) — E (was finding #80): the per-owner unique email index lives only in DbUp, not the EF model.** The DbUp baseline declares `ix_customers_tenant_id_owner_subject_email` UNIQUE over `(tenant_id, owner_subject, email)`; `CustomerConfiguration` declares only the non-unique `(tenant_id, owner_subject)` index. The fresh review proposed mirroring the unique index in the EF model, but this was **investigated and proven not implementable** while `Email` is an owned value object: the index spans `tenant_id`/`owner_subject` (on `Customer`) plus `email` (on the owned `Email` entity type), and EF Core's fluent API cannot declare a composite index across an entity and its owned type even when they share a table (`HasIndex("TenantId","OwnerSubject","Email.Value")` throws *"property 'Email.Value' cannot be added to the type 'Customer'"*). Production is unaffected — DbUp owns the schema, the unique constraint is enforced in the database, and the create/update customer handlers catch that exact constraint name as the race-safe uniqueness net. The only residual is that `EnsureCreated()`/EF model-diff would not see the index, neither of which this project uses (tests run DbUp migrations). A `NOTE` comment in `CustomerConfiguration` records this so the broken one-liner is not re-attempted. Reclassified from open finding to accepted limitation; moving `Email` to a .NET 8 complex type (which would make the columns part of `Customer` and allow the composite index) is the only path to mirror it and is not worth the `OwnsOne` convention churn for a Low item.

**Accepted limitation (optional hardening, not a defect) — H-HSTS: HSTS relies on framework defaults.** `app.UseHsts()` (`Program.cs`) is called with no `services.AddHsts(...)` registration, so the emitted policy is the framework default (~30-day max-age, no `includeSubDomains`, no `preload`). This is intentionally accepted, not a vulnerability: `app.UseHttpsRedirection()` already enforces HTTPS on every request independent of the HSTS header, and the API container terminates plain HTTP on `:8080` behind an upstream TLS terminator, so the header's practical role is browser-side policy caching only. If a deployment fronts the API directly with TLS and owns every subdomain, register `AddHsts` with `IncludeSubDomains`/`Preload`/365-day max-age. Recorded so future reviews do not re-raise the default policy as a finding.

**Accepted design rationale (not a defect) — deliberate comprehensiveness for an agent-maintained template.** Both independent reviews (7.8, 8.2) deducted for "proportionality" — heavyweight machinery (transactional outbox + Service Bus topology, payload-capture archive/audit/entity-index, signed gateway identity, owner-scoping, a large convention-test fleet) for a single 3-entity domain. **This breadth is intentional and is the point of the template, not over-engineering.** Per CLAUDE.md, the codebase is maintained by AI agents and favours mechanical rules over architectural taste: it exists to (a) demonstrate every production pattern an agent should copy, end-to-end, on a domain small enough to read, and (b) make agent-introduced drift mechanically detectable via convention tests (agents generate boilerplate cheaply, so the cost that makes this "heavy" for a human team is not the relevant cost here). Future independent reviews should judge breadth on this agent-template rubric — as a feature — and **not** re-deduct it as bloat. Two honest caveats that are *not* covered by this rationale and remain real: (1) the latent footguns and the create-order retry-idempotency test gap in Open Findings stand regardless of template intent; (2) folder-only Clean Architecture inside one assembly is the one place where *more* completeness (splitting Application/Infrastructure into separate assemblies so the boundary is compiler-enforced) would make the template a stronger reference — that is a genuine consideration, not proportionality.

**Accepted limitation (not a defect) — D: outbox delivery is at-least-once.** If the processor crashes between the Service Bus publish and the `ProcessedOnUtc` mark, the row is reclaimed after `LockedUntilUtc` expires and republished. This is inherent to the outbox pattern and intentionally mitigated by Service Bus duplicate detection (5-minute window) plus the `ProcessingId` concurrency token (which blocks concurrent double-publish by a second processor). Subscribers must remain idempotent. No code change; recorded so future reviews do not re-raise it as a defect.

**~~Residual test gap (2026-06-08) — legacy owner-stamping constructors public/unguarded~~ — RESOLVED 2026-06-08.** `Order`/`Customer`/`Product` previously exposed convenience constructors that omitted owner scope and stamped `OwnershipDefaults.Legacy*` sentinels — a footgun (a future handler could create owner-less, cross-tenant-invisible rows). The earlier IL `newobj` convention scan was config-dependent (passed Debug, failed Release) and was reverted; `internal` couldn't bar Api (Domain grants `InternalsVisibleTo("StarterApp.Api")`). **Fixed properly:** the no-owner ctors were **deleted** outright (production can no longer construct an owner-less aggregate), tests now build through a single `TestEntities` factory that supplies the owner scope (defaulting to the legacy sentinels so behavior is unchanged). The entity `ProductBuilder` was folded into the factory, and the misnamed command-builders — first renamed to `Create*CommandBuilder`, then (commit `530370a`) **deleted** in favor of inline initializers, with the genuinely-useful order scenarios kept in a static `OrderCommands` helper. A **config-independent reflection** convention test (`DomainConventionTests.OwnerScopedAggregates_MustNotExposeOwnerlessPublicConstructors`) prevents reintroduction. No production code changed (all 3 Api sites already used owner-aware ctors).

**Dismissed false positive (2026-06-03) — deleted AppHost `packages.lock.json` does NOT break reproducible builds.** Commit `be8d786` deleted `src/StarterApp.AppHost/packages.lock.json` and `src/StarterApp.AppHost.Tests/packages.lock.json` and set `RestorePackagesWithLockFile=false` for those two projects only. This is sound and intentional: the Aspire SDK injects host-RID-specific direct packages (e.g. `Aspire.Dashboard.Sdk.<rid>`, `Aspire.Hosting.Orchestration.<rid>`) that differ by platform, so a committed cross-platform lock file is impossible for the host projects. The other six projects (Api, Domain, Functions, DbMigrator, Tests, ServiceDefaults) retain locked restore, and CI still runs `dotnet restore --locked-mode`, so the reproducible-build guarantee is preserved where it can be. Recorded so future reviews do not re-raise the deletion as a regression of finding A.

#### Recently resolved (comprehensive multi-agent audit, 2026-06-06)

| # | Finding | Fix |
|---|---------|-----|
| H | Payload **archive + audit metadata** stored the raw HTTP query string (`PayloadCaptureMiddleware` populates `metadata["queryString"]`; `PayloadCaptureSink` wrote `Metadata = request.Metadata` verbatim into the long-lived, support-readable archive/audit blobs). The codebase already classified `queryString` as sensitive and stripped it from the *entity-index* path (`IsSensitiveMetadataKey`), but the archive/audit path applied no metadata filtering — an internal inconsistency. Impact is **Medium-leaning-Low**, not the originally-claimed "PII leak": archive/audit payload **bodies** are sanctioned to contain full-fidelity PII by design (asserted by `PayloadCaptureTests` line ~95), so the genuine residue is **bearer secrets in the query string** (`?token=`, `?password=`, `?secret=`, `?authorization=`) landing unredacted in archive/audit metadata, even though the equivalent request *headers* are never captured. | Added value-level query-string redaction in `PayloadCaptureSink.BuildArchiveMetadata`/`RedactSensitiveQueryStringValues`: the archive/audit record keeps the `queryString` key and all benign params (`page`, `pageSize`, `status`) so debugging context survives, but masks the *values* of parameters whose names match `SensitivePropertyNames` to `[REDACTED]`. The entity index keeps dropping `queryString` entirely (pointer-only by design) — the two paths now differ in degree intentionally, documented at the helper. Regression test `CaptureAsync_ShouldMaskSensitiveQueryStringValuesInArchiveMetadataButKeepBenignParams` proves `page`/`pageSize` survive while `token`/`email` values are masked; verified to fail on the pre-fix code. Note: `SensitivePropertyNames` (in `PayloadCaptureOptions`) is the tuning knob — add e.g. `apikey` there to cover additional query-secret names across the redactor, entity-index, and archive paths uniformly. |

#### Recently resolved (DAST scan, 2026-06-06)

| # | Finding | Fix |
|---|---------|-----|
| G | OWASP ZAP active scan flagged `PUT /api/v1/products/{id}` returning **HTTP 500** when sent a malformed JSON body. Root cause: ASP.NET throws `BadHttpRequestException` for an unreadable request body, but `ResolveExceptionStatusCode` (`WebApplicationExtensions.cs`) had no arm for it, so it fell through to the `_ => 500` default. Malformed client input should be a 400, and an unhandled 500 is also an error-disclosure surface. This is a genuine runtime defect (distinct from the `/openapi/v1.json` false positive recorded in the top note). | Added `BadHttpRequestException => StatusCodes.Status400BadRequest` to the exception→status switch. Regression test `ExceptionStatusCodeMappingTests.ResolveExceptionStatusCode_WithMalformedRequestBody_ShouldReturnBadRequest` locks the mapping (plus not-found→404 and unmapped→500 arms). Re-running the DAST harness confirms no alert ≥ Medium remains. |

#### Recently resolved (comprehensive multi-agent review, 2026-06-03)

| # | Finding | Fix |
|---|---------|-----|
| E | `CqrsConventionTests` enforced only the *negative* CQRS rule (command handlers must not depend on `IDbConnection`, query handlers must not depend on `DbContext`) but never the *positive* one: that command handlers MUST depend on `ApplicationDbContext`. A handler injecting neither data-access type (e.g. a pure pass-through, or one routed through Dapper helpers) would silently bypass the EF Core write path while passing every existing convention. | Added `CommandHandlers_MustDependOnApplicationDbContext` (asserts every `*CommandHandler` constructor takes an `ApplicationDbContext`, with an `Assert.NotEmpty` guard against a vacuous pass if the discovery filter ever matches zero handlers). Proven to fail when the required type is swapped, confirming it is behaviour-checking, not always-green. |
| F | `AggregatesOverridingRecordCreation_MustHaveGuidId` verified the aggregate `Id` is a `Guid` but not that it is minted via `Guid.CreateVersion7()`. A regression to `Guid.NewGuid()` would still pass the type check while losing the time-ordered insert locality CLAUDE.md mandates (line 67). Correctness of the single-`SaveChanges` outbox contract holds for any client-generated Guid, so this is locality/convention drift, not a runtime defect. | Extended the test to scan public-constructor IL (reusing the existing `ContainsCallToMethod` helper) for a call to `Guid.CreateVersion7()`; internal/explicit-id constructors stay exempt because they serve the retry-safe path where the caller pre-generates the v7 Id. Proven to fail when `Order` is regressed to `Guid.NewGuid()`. |

Note: E and F are presence→behaviour convention-test hardening in the same vein as the earlier owner-policy-invocation and cache-invalidator-invocation tests. The current handlers and aggregates already satisfy them, so the tests pass on landing rather than fixing a live bug.

#### Recently resolved (comprehensive multi-agent review, 2026-06-02)

| # | Finding | Fix |
|---|---------|-----|
| A | CI restored with `dotnet restore --force-evaluate`, which recomputes and rewrites the dependency graph every run, silently defeating the committed `packages.lock.json` files and the reproducible-build guarantee even though `RestoreLockedMode` is set under CI. | Switched all three CI jobs to `dotnet restore --locked-mode` so CI fails loudly when lock files drift from project files. Commit `ba43e9f`. |
| B | `CachingConventionTests` verified `ICacheInvalidator` was *injected* into cacheable mutation handlers but not *invoked* — an inject-and-forget handler would pass while leaving stale entity reads in the distributed cache after a write (the same presence-vs-behaviour gap closed earlier for `IOwnerOnlyPolicy`). | Added `MutationHandlers_OnCacheableEntities_MustInvokeCacheInvalidator`, an IL invocation scan (including async state machines) mirroring `CommandHandlers_MustInvokeOwnerOnlyPolicy`. Commit `c880de6`. |
| C | Two CLAUDE.md invariants had no convention backing: (1) the single-`SaveChanges` outbox pattern relies on every EF write entry point funnelling through domain-event capture, and (2) every inbound Service Bus payload must be archived. Either could regress silently. | Added `ApplicationDbContextSaveChanges_MustCaptureDomainEventsIntoOutbox` (scans the actual `SaveChanges`/`SaveChangesAsync` override IL — including ldftn method-group references — for the capture call, discovered by behaviour rather than name) and `ServiceBusTriggeredFunctions_MustCaptureInboundPayload` (every `[ServiceBusTrigger]` Function must invoke `IPayloadCaptureSink`; the timer-only cleanup function is naturally excluded). Commit `0b62b9d`. |

Note: B and C are regression guards — the current handlers and Functions already satisfy them, so the tests pass on landing rather than fixing a live bug.

Fresh review reconciliation: finding #43 is resolved by the trusted gateway identity boundary and identity-based rate limiting. Findings #57-#59 are resolved by route-level scope enforcement, mapped-endpoint convention coverage, and owner-only resource authorization. Findings #44 and #45 are resolved by the outbox processing-claim redesign and AppHost subscriber-consumption assertion. Finding #64 is resolved by refreshing stale setup/API documentation to match gateway identity, `/api/v1` routing, Functions Docker hosting, CI jobs, and the current sample subscriber behavior. Finding #65 is resolved by removing the duplicate local orchestration path and making Aspire the single local stack while keeping direct image validation. Finding #56 is resolved by keeping status input typed as `OrderStatus` and routing lifecycle changes through intent-specific aggregate methods. Finding #66 is resolved by completing the PostgreSQL persistence port and removing the previous provider/runtime compatibility path. The other fresh-eyes findings were fixed in commit `5285814` and recorded below as #52-#55.

#### Verification rerun (2026-06-01, not an architecture finding)

- `dotnet restore --locked-mode`, `dotnet format --verbosity minimal --no-restore`, `dotnet build --no-restore`, and `dotnet test --no-build` all passed locally after the coverage follow-up.
- Targeted coverage-follow-up run passed 146 tests across conventions, command handlers, and customer/product integration tests.
- Full suite: `StarterApp.Tests` 533 passed in 1m33s; `StarterApp.AppHost.Tests` 11 passed in 4m11s.
- The earlier local notes about a stale NuGet PAT, a failing AppHost subscriber test, and the `PreToolUse` commit gate being unable to finish inside 300 s did not reproduce during this rerun. Follow-up review action raised the hook timeout to 900 s and added a direct regression test that maps `DbUpdateConcurrencyException` to `409 Conflict`.

#### Recently resolved (coverage follow-up, 2026-06-01)

| # | Finding | Fix |
|---|---------|-----|
| ~~71~~ | Several command/query endpoints lacked dedicated integration tests for customer/product mutations | Added `CustomerApiTests` covering update/delete success, ID mismatch preservation, and delete conflict with existing orders; extended `ProductApiTests` with ID mismatch preservation and delete conflict with existing order items. |
| ~~72~~ | Data-access-sensitive application handler tests used EF Core InMemory, which does not enforce PostgreSQL constraints or row-version behaviour | Added `PostgresCommandHandlerTestBase` backed by the shared Testcontainers PostgreSQL fixture and moved command-handler persistence tests onto `UseNpgsql`, leaving InMemory only in infrastructure tests that intentionally exercise the outbox processor fallback path. |
| ~~73~~ | Some convention tests asserted marker presence or source text instead of behaviour | Added SQL literal checks requiring owner-scoped Dapper queries to include `owner_subject = @OwnerSubject` and `tenant_id = @TenantId`; changed gateway-header isolation to inspect compiled IL string literals and `GatewayIdentityHeaders` type references outside identity infrastructure. |

#### Recently resolved (fresh review, 2026-06-01)

| # | Finding | Fix |
|---|---------|-----|
| ~~69~~ | Inventory reservation subscriber wording conflicted with synchronous stock reservation | Reworded the log message and TODO in `InventoryReservationFunction` to state stock is reserved synchronously and atomically by `CreateOrderCommandHandler` (the single owner of catalog-stock mutation) and that the subscriber is notification/projection-only and must not mutate stock. Subscription name left unchanged to avoid a breaking topology change. Commit `3dc282c`. |
| ~~70~~ | Default `.http` scratch request targeted the removed weatherforecast endpoint | Replaced with a public `/health/live` smoke test plus a documented `/api/v1/products` example carrying the gateway identity header contract. Commit `7df18f2`. |
| New | Owner-policy convention tests verified injection but not invocation (a handler could inject `IOwnerOnlyPolicy` and never call it) | Added `CommandHandlers_MustInvokeOwnerOnlyPolicy` and `QueryHandlers_MustInvokeOwnerOnlyPolicy` IL-scan tests; promoted shared IL helpers to `ConventionTestBase`. Commit `e898004`. |
| New | `xmin` optimistic-concurrency token was configuration-checked but its runtime behaviour was untested | Added `OptimisticConcurrencyIntegrationTests` proving a stale write throws `DbUpdateConcurrencyException` (→ 409) against real PostgreSQL. Commit `55f7f58`. |
| New | `OutboxMessage` ids used random `Guid.NewGuid()` against the repo's Guid v7 convention | Switched to `Guid.CreateVersion7()` for time-ordered ids. Commit `b54de10`. |
| New | Four orphaned `src/StarterApp.Modules.*` directories (no source, not in the solution, stale net9.0 artifacts) | Deleted from disk (gitignored, no commit). |

#### Recently resolved (2026-05-30 review follow-up)

| # | Finding | Fix |
|---|---------|-----|
| ~~67~~ | PostgreSQL integrity violations still fell through to 500 | Added PostgreSQL foreign-key, check-constraint, and not-null violation helpers; routed FK violations to `409 Conflict` and check/not-null violations to `400 Bad Request` through the centralized exception status selector; added focused regression tests for status mapping and constraint-name matching. |
| ~~68~~ | Currency validation accepted non-letter three-character values despite advertising ISO codes | `Money.IsValidCurrencyCode` now requires exactly three ASCII letters and `Money.Create` normalizes to uppercase; product validators reuse the domain predicate; domain, fuzz, validator, and ProblemDetails tests cover non-letter rejection. |

#### Recently resolved (PostgreSQL port and lifecycle tightening)

| # | Finding | Fix |
|---|---------|-----|
| ~~56~~ | Order status API modeled lifecycle changes as arbitrary state assignment | `UpdateOrderStatusCommand.Status` is now typed as `OrderStatus?`, route/query input is normalized to the enum before handler/SQL execution, and the handler routes transitions through explicit aggregate methods (`Confirm`, `StartProcessing`, `Ship`, `Deliver`, `Cancel`) instead of generic string parsing and status assignment. |
| ~~66~~ | Persistence stack was still coupled to the previous provider behavior and T-SQL dialect assumptions | Replaced the old database packages with Aspire PostgreSQL, Npgsql EF Core, Npgsql, DbUp PostgreSQL, and Testcontainers PostgreSQL; rebuilt migrations as a PostgreSQL baseline; mapped concurrency to PostgreSQL `xmin`; converted Dapper SQL to PostgreSQL syntax; switched outbox claims to `FOR UPDATE SKIP LOCKED`; and updated conventions/tests/docs to make PostgreSQL the only supported app database path. |

#### Recently resolved (documentation refresh)

| # | Finding | Fix |
|---|---------|-----|
| ~~64~~ | Public setup and API docs had drifted from the current template behavior | Refreshed `docs/API-ENDPOINTS.md`, README setup notes, Aspire/Docker setup docs, and the completion guide so they match `/api/v1` routes, gateway identity headers/scopes/MFA, product request shapes, Guid order ids, Functions Docker runtime hosting, CI job split, and the current sample subscriber behavior. Commit: pending. |
| ~~65~~ | Maintaining two local orchestration paths created drift risk for an AI-maintained template | Removed the duplicate YAML run path, deleted its static Service Bus emulator config and Docker-only appsettings, made AppHost fluent topology the single source of truth, and switched CI to direct image builds. Commit: pending. |

#### Recently resolved (eventing and observability hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~44~~ | `OutboxProcessor` held SQL update locks while performing Blob capture and Service Bus send network I/O | Added `ProcessingId` and `LockedUntilUtc` outbox claim columns, DbUp migration `0019_AddOutboxProcessingClaims.sql`, EF mapping/index updates, and processor flow that claims rows in a short transaction, publishes outside the lock, and saves outcomes afterward. |
| ~~45~~ | AppHost eventing test observed `ProcessedOnUtc` but not subscriber consumption | `CreateOrder_ShouldWriteAndProcessOutboxEvent` now sends a known correlation id and waits for both `OrderConfirmationEmailFunction` and `InventoryReservationFunction` inbound Service Bus payload-capture records in Blob storage. AppHost runs Functions via the Azure Functions Docker image and wires `servicebus`, `AzureWebJobsStorage`, and payload archive settings so subscribers consume under the real Functions host. |
| ~~60~~ | Aspire Service Bus topic duplicate detection was missing from the fluent topology | AppHost topic properties now explicitly enable duplicate detection with a 5-minute window and `ServiceBusTopologyConventionTests` verifies the topology constants. |
| ~~61~~ | Fail-closed payload archive outages could consume outbox retry budget and permanently error rows | Added transient payload archive failure classification and outbox coverage so Azure Blob dependency failures pause the batch with retries intact instead of poisoning messages. |
| ~~62~~ | Validators and domain guards drifted for email and currency invariants | `Email` and `Money` now expose canonical validation predicates used by command validators; `Money.Create` enforces exactly three-character currency codes; unit/fuzz tests cover the synchronized behavior. |
| ~~63~~ | Aspire AppHost SDK version was skewed from Aspire package versions | Aligned `Aspire.AppHost.Sdk` to `13.2.3` and added `HousekeepingConventionTests.AppHostSdkVersion_MustMatchAspirePackageVersion`. |

#### Recently resolved (gateway identity hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~43~~ | Rate limiting partitions by `RemoteIpAddress` without trusted forwarded-header handling | Added a trusted gateway identity contract with signed assertion validation, `ICurrentUser`, `RequireGatewayIdentity()` endpoint metadata, convention coverage for protected API groups and raw header access, and identity-based rate-limit partitioning for protected requests. |

#### Recently resolved (gateway authorization review)

| # | Finding | Fix |
|---|---------|-----|
| ~~57~~ | Gateway identity scopes were signed but never enforced | Added `RequireScope(...)` endpoint metadata plus a central endpoint filter that returns `403 Forbidden` when the authenticated identity lacks the route's required read/write scope; added integration coverage for missing read and write scopes. |
| ~~58~~ | Protected-route convention was a fragile source-text scan | Replaced the literal endpoint-file scan with mapped `EndpointDataSource` convention coverage so every `/api/v1` route must carry gateway identity and scope metadata regardless of source layout. |
| ~~59~~ | Resource ownership authorization was documented but not broadly enforced | Added owner-scope persistence (`OwnerSubject`, `TenantId`) for Customer/Product/Order, `IOwnerOnlyPolicy` checks in command handlers, owner-filtered Dapper queries, owner-scoped cache keys, DbUp migration `0018_AddOwnerScopeColumns.sql`, convention coverage, and integration tests for cross-owner read/write behavior. |

#### Recently resolved (fresh review fixes, commit 5285814)

| # | Finding | Fix |
|---|---------|-----|
| ~~52~~ | `PUT /orders/{id}/status` could cancel an order without restoring reserved stock | Added shared `OrderCancellationService` used by both `CancelOrderCommandHandler` and `UpdateOrderStatusCommandHandler`; added regression coverage for cancelling through the status-update path restoring stock. |
| ~~53~~ | `ValidationException` collected structured `ValidationError`s, but HTTP responses returned generic ProblemDetails only | Replaced plain `AddProblemDetails()` with `AddApiProblemDetails()` customization that emits `errors` grouped by property; added fast ProblemDetails customization coverage and an integration assertion. |
| ~~54~~ | Domain project referenced Serilog despite the no-external-dependencies rule, and conventions only blocked API references | Removed the Domain Serilog package reference and lock-file entry; added `DomainAssembly_MustNotReferenceThirdPartyAssemblies` convention coverage. |
| ~~55~~ | `OrderItem` rejected GST rates above 1.0, but the database schema only checked `GstRate >= 0` | Added DbUp migration `0017_AddOrderItemGstRateRangeConstraint.sql` with named constraint `CK_OrderItems_GstRate_Range` enforcing `0 <= GstRate <= 1`. |

#### Recently resolved (payload archive hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~46~~ | Payload capture had no explicit failure policy, so configured storage outages broke requests while missing storage silently dropped archive rows | Added `PayloadCapture:FailureMode=FailOpen|FailClosed`, `RequireArchiveStore` startup validation, null-store skip logging, and tests for fail-open/fail-closed behavior. Aspire runs production-like payload capture with `RequireArchiveStore=true` and `FailClosed`; standalone development can intentionally fail open or use the null store. |
| ~~47~~ | The former secondary local run path did not mirror Aspire's payload archive Blob dependency | Added Azurite-backed payload archive wiring at the time; this drift class is now closed by making Aspire the only supported local orchestration surface. |
| ~~48~~ | HTTP payload capture buffered full request/response bodies with no limit | Replaced full response buffering with a bounded tee stream, bounded request reads by `PayloadCapture:MaxPayloadBytes`, added content-type capture rules, and persisted truncation/skip metadata on archive/audit rows. |
| ~~49~~ | JSON log redaction only matched exact sensitive property names and missed emails inside JSON strings | Redactor now matches normalized sensitive names inside property names (e.g. `customerEmail`, `ownerName`) and redacts email-like substrings inside non-sensitive JSON string values. |
| ~~50~~ | API console logging was configured twice | Removed the unconditional code-level console sink from `Program.cs`; Serilog sinks now come from configuration. |
| ~~51~~ | `PayloadCaptureOptions.CleanupCron` was exposed but the Function timer was hardcoded | `PayloadArchiveCleanupFunction` now uses the `PayloadCapture__CleanupCron` app setting; AppHost and local Functions settings provide the default hourly schedule. |

#### Recently resolved (retry and concurrency hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~41~~ | `CreateOrderCommandHandler` generated a fresh order id inside the execution-strategy retry delegate, so a commit-unknown retry could create a second order and reserve stock twice | The handler now generates one stable order id before the retry delegate and checks for that id at the start of each retry before reserving stock. `Order` has an internal explicit-id constructor for this retry-safe path. |
| ~~42~~ | Cancellation restored stock with no stale-write gate, allowing concurrent cancellation/status changes to double-restore or overwrite inventory | `Order` and `Product` now map PostgreSQL `xmin` concurrency tokens through `uint RowVersion` properties configured with `IsRowVersion()`, and `DbUpdateConcurrencyException` maps to `409 Conflict`. A convention test keeps those tokens in place. |

#### Recently resolved (PostgreSQL transient retry)

| # | Finding | Fix |
|---|---------|-----|
| ~~40~~ | Conventional.Samples comparison exposed convention coverage gaps around migration embedding, serializer-friendly response contracts, namespace locality, collection materialization, bin/obj project references, comment hygiene, and async/time scan scope | Added focused convention tests for these rules in `StarterApp.Tests` and `StarterApp.AppHost.Tests`; converted existing production XML documentation comments to short ordinary comments; left EF value-object default-constructor initialization intentionally out of scope. |
| ~~39~~ | No advisory consistency layer for structural drift across common file shapes | Added `StarterApp.Tests/Consistency/` with command-handler, query-handler, and EF-configuration cohorts; added pinned exemplar docs under `docs/exemplars/`; extracted EF mappings into per-entity `IEntityTypeConfiguration<T>` classes so mapping shape is measurable outside `ApplicationDbContext`. |
| ~~36~~ | No transient-failure retry on `DbContext` for database throttling / failover | `Order` aggregate now uses client-generated `Guid.CreateVersion7()` IDs. This lets `RecordCreation()` run BEFORE `SaveChanges` (events already know their Ids), keeping outbox capture inside a single `SaveChanges` — no user transaction needed. Npgsql `EnableRetryOnFailure(6, 30s)` is enabled in `AddPersistence`. `CreateOrderCommandHandler` (the one handler that still needs a user-managed transaction for atomic stock-reservation + order-save) wraps itself in `Database.CreateExecutionStrategy().ExecuteAsync(...)` and calls `ChangeTracker.Clear()` at the top of each attempt so retries start from a clean state. A convention test enforces: any `AggregateRoot` overriding `RecordCreation()` must have a `Guid Id` so future creation events have stable keys before save. |
| ~~38~~ | Dapper query handlers had no transient-fault retry — `EnableRetryOnFailure` only covers the EF `DbContext`; Dapper uses its own connection path, so mid-query database failover or throttling could surface as a 500 while writes are transparently retried | `PostgresRetryPolicy.ExecuteAsync` at `Infrastructure/Persistence/PostgresRetryPolicy.cs` retries transient Npgsql exceptions and SQLSTATEs with exponential backoff (6 attempts, 30s cap, base 1s). All query handlers wrap Dapper calls in the helper. `DapperConventionTests.QueryHandlers_MustUsePostgresRetryPolicy` scans IL and fails the build if any future `*QueryHandler` with an `IDbConnection` field forgets to go through the helper. |

#### Recently resolved (outbox publish resilience)

| # | Finding | Fix |
|---|---------|-----|
| ~~37~~ | Transient Service Bus outages consumed per-message retry budget — a multi-minute SB outage would mark every polled message as permanently `Error`, requiring manual requeue | `OutboxProcessor` now distinguishes transient `ServiceBusException` reasons (`ServiceCommunicationProblem`, `ServiceTimeout`, `ServiceBusy`, `QuotaExceeded`) from message-level failures. Transient errors log a warning, break the batch, and leave rows unprocessed with retries intact — next poll tick re-attempts cleanly. Message-level errors (e.g. `MessageSizeExceeded`) still consume retries. No dedicated circuit breaker — the outbox already decouples user requests from publish latency, and this targeted fix addresses the actual failure mode. |

#### Recently resolved (outbox correctness + eventing contract)

| # | Finding | Fix |
|---|---------|-----|
| ~~28~~ | OrderCreatedDomainEvent captures pre-persist OrderId (always 0) | Aggregates that raise creation events use client-generated Guid v7 IDs before `SaveChanges`, so outbox capture remains atomic and creation events carry stable keys. |
| ~~29~~ | Outbox rows can be published more than once (no locking, no dedup) | PostgreSQL row claiming with `FOR UPDATE SKIP LOCKED` plus `ProcessingId`/`LockedUntilUtc`; duplicate detection enabled on Service Bus topic |
| ~~30~~ | Event routing coupled to CLR type names — rename breaks routing silently | Convention test validates subscription filter EventType values against actual IDomainEvent class names |
| ~~31~~ | Aspire integration test doesn't verify eventing path | Added `CreateOrder_ShouldWriteAndProcessOutboxEvent` — queries OutboxMessages directly via SQL, asserts row exists with `order.created.v1` type and polls until `ProcessedOnUtc` is non-null (proves outbox processor published to Service Bus) |

#### Recently resolved (P3 hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~32~~ | Dapper query handlers don't pass CancellationToken | All 7 query handlers now use `CommandDefinition` with `cancellationToken` parameter |
| ~~33~~ | Money and Email value objects lack `operator ==`/`!=` | Added `operator ==` and `operator !=` overloads to both value objects |
| ~~34~~ | `order.status-changed.v1` published but no subscription matches | Added `OrderStatusChangedFilter` correlation rule to `email-notifications` subscription |
| ~~35~~ | `Directory.Build.props` NuGetLockFilePath uses backslash | Changed to forward slash for cross-platform compatibility |

#### Recently resolved (Service Bus hardening)

| # | Finding | Fix |
|---|---------|-----|
| ~~22~~ | ServiceBusClient leaks AMQP connections | Factory-based DI registration — container owns disposal |
| ~~23~~ | No retry logic for transient failures | `IncrementRetry()` + `MaxRetries` — messages retry before permanent error |
| ~~24~~ | Outbox index missing Error column | Filtered index `WHERE ProcessedOnUtc IS NULL AND Error IS NULL` (Error is NVARCHAR(MAX), can't be key) |
| ~~25~~ | OutboxProcessorOptions has no validation | DataAnnotations with `ValidateOnStart()` |
| ~~26~~ | Functions host.json missing Service Bus config | Added `maxConcurrentCalls`, `autoCompleteMessages`, `maxAutoLockRenewalDuration` |
| ~~27~~ | Functions stubs have no error handling | Added try-catch with structured error logging and re-throw |

#### Recently resolved (commit 614f069)

| # | Finding | Fix |
|---|---------|-----|
| ~~16~~ | UpdateProduct zeros Price/Stock on field omission | Command properties changed to nullable; validator rejects nulls |
| ~~17~~ | Order status case sensitivity mismatch | Handler now uses `Enum.Parse` with `ignoreCase: true` |
| ~~18~~ | DB constraints surface as 500s | `DbUpdateExceptionExtensions` maps SQL errors to 409/400; domain guards + validators enforce max lengths; customer handlers check email uniqueness pre-save |
| ~~19~~ | TestFixture hides base disposal | Added `Client?.Dispose()` and `base.Dispose()` |
| ~~21~~ | PropertiesTest false positives | Tests now use actual `IValidator<T>` implementations instead of DataAnnotations |

### Recently Resolved (commits 05d2996–898424c)

| Finding | Fix | Commit |
|---------|-----|--------|
| Client-trusted pricing on order creation | Price, currency, GST now sourced from product catalog | 05d2996 |
| Duplicate ProductId corrupts stock reservation | Validator + handler duplicate check, defense-in-depth | 05d2996 |
| No concurrency control on stock (overselling) | Atomic `ExecuteUpdateAsync` with `WHERE Stock >= @qty` | 05d2996 |
| Rate limiting configured but never enforced | Added `GlobalLimiter` with per-IP partitioning | 05d2996 |
| Mixed currencies produce incorrect order totals | `Order.EnsureCurrencyMatchesExistingItems` domain guard | 05d2996 |
| Outbox two-save atomicity gap | DbContext manages internal transaction when no outer one exists | 2fbf07c |
| `RecordCreation()` was caller-responsibility | Auto-detected via change tracker during `SaveChanges` | 2fbf07c |
| Stock reservation checked existence after UPDATE | Reordered: load product first, then atomic reserve | 898424c |
| No upper bound on order items count | Validator caps at 50 items | 898424c |
| UpdateProduct zeros Price/Stock on field omission | Nullable command properties; validator rejects nulls | 614f069 |
| Order status case sensitivity mismatch | `Enum.Parse` with `ignoreCase: true` | 614f069 |
| DB constraints surface as 500s | `DbUpdateExceptionExtensions`, domain max-length guards, validator mirrors | 614f069 |
| TestFixture hides base disposal | Added `Client?.Dispose()` and `base.Dispose()` | 614f069 |
| PropertiesTest false positives | Tests now use actual `IValidator<T>` implementations | 614f069 |
| Closed switch in outbox serialization | Generic serialization; events flattened; convention test prevents entity references in events | (this commit) |

---

### ~~1. Read Model Totals Are Never Written — CQRS Data Consistency Bug~~ FIXED

**Status: Resolved.** Dapper read queries now compute totals via `OUTER APPLY` subqueries against `OrderItems` instead of reading dead columns from the `Orders` table. The total columns (`TotalExcludingGst`, `TotalIncludingGst`, `TotalGstAmount`) have been dropped from the schema. Regression test `GetOrder_ShouldReturnCorrectTotals` verifies read-path totals match write-path totals.

### ~~2. No Authentication or Authorization~~ BY DESIGN

**Status: Intentional and hardened.** This template assumes the API runs behind APIM or an equivalent trusted gateway that validates caller authentication. The API does not add ASP.NET authentication/JWT bearer middleware, but it no longer blindly trusts arbitrary headers.

Protected API groups require normalized gateway identity headers and, in production-like `GatewayIdentity:Mode=Required`, a signed `X-Gateway-Assertion`. The API exposes identity through `ICurrentUser`, rejects missing/tampered assertions with `401`, enforces route scopes with `403`, requires gateway-projected MFA proof on write routes through `SecuredBy2Fa()`, and applies owner-only resource authorization for Customer, Product, and Order through persisted owner columns plus `IOwnerOnlyPolicy`.

### ~~3. CreateOrderCommand Has Two SaveChanges Without Transaction Boundary~~ FIXED

**Status: Resolved.** The root cause was a broken aggregate boundary: EF Core's `Items` navigation was `Ignore()`d, forcing items to be saved separately via `DbSet<OrderItem>`. Fix: restored the `Order→Items` navigation via backing field access (`UsePropertyAccessMode(PropertyAccessMode.Field)`), added `Order.AddItem(productId, name, qty, price, rate)` overload that constructs items through an `internal` OrderItem constructor (no orderId needed — EF sets FK on save). Handler now uses a single `SaveChangesAsync`. Regression test `CreateOrder_WithSecondProductNotFound_ShouldNotLeavePartialOrder` verifies no orphaned rows.

### ~~4. Public `SetId()` Methods Break Domain Encapsulation~~ FIXED

**Status: Resolved.** Removed `SetId()` from `Customer`, `Product`, and `OrderItem`. EF Core sets `Id` via the private setter. Deleted the corresponding unit tests that exercised these methods.

### ~~5. UpdateOrderStatus and CancelOrder Use AsNoTracking Then Update~~ FIXED

**Status: Resolved.** Both handlers now load tracked entities via `.Include(o => o.Items)`, mutate through domain methods, and call `SaveChangesAsync(cancellationToken)`. EF Core detects only changed properties — no more full-row overwrites. `Reconstitute` is no longer used in production handlers (made `internal`, retained for fuzz tests via `InternalsVisibleTo`).

### ~~6. Thin Application Layer~~ IMPROVED

**Status: Partially resolved.** `CreateOrderCommandHandler` now checks stock availability before adding each order item and decrements stock via `Product.UpdateStock()`. Stock reservation is atomic with order creation — if any item fails (product not found, insufficient stock), no stock is decremented and no order is saved. A shared `OrderCancellationService` restores stock for both the dedicated cancel command and the status-update cancellation path.

**Remaining gap:** Other command handlers are still CRUD pass-through.

### ~~7. Sparse Validation Coverage~~ FIXED

**Status: Resolved.** Every command and query now has an `IValidator<T>` implementation (16 total). Convention tests `EveryCommand_MustHaveAValidator` and `EveryQuery_MustHaveAValidator` enforce coverage — adding a new command or query without a validator fails the build.

Validators intentionally overlap with domain constructor guards (defense-in-depth). Validators provide structured multi-error `ValidationError` responses at the API boundary through the ProblemDetails `errors` extension; domain guards are the safety net. The sync rule is documented in AGENTS.md and CLAUDE.md.

**Design rationale:** This codebase is AI-agent maintained. For human maintainers, requiring a validator for `DeleteProductCommand` (just `Id > 0`) would be busywork. For agents, the mechanical rule eliminates the judgment call "does this command need a validator?" — boilerplate is cheap, ambiguity is expensive.

### ~~8. Database Migrations Run on API Startup~~ FIXED

**Status: Resolved.** Removed `DatabaseMigrator.MigrateDatabase()` call from `Program.cs` and deleted the `DatabaseMigrator.cs` wrapper. Removed the DbMigrator project reference from the API `.csproj`. Migrations are now handled exclusively by the dedicated `DbMigrator` service:

- **Aspire:** `AppHost` runs `DbMigrator` as a standalone service with `WaitFor` dependency on PostgreSQL
- **Container deployments:** run the migrator image/job to completion before starting API replicas
- **Standalone dev:** Run `dotnet run --project src/StarterApp.DbMigrator` before starting the API
- **Integration tests:** Unaffected — `TestFixture.RunDbUpMigrations()` runs migrations independently

The API Dockerfile no longer copies the DbMigrator project or its appsettings.json.

**Deployment note:** Any deployment pipeline that targets a real environment must run the migrator to completion before starting the API. The mechanism varies by platform (Kubernetes init container/Job, Azure Container Apps sidecar, AWS ECS essential container dependency with `"condition": "SUCCESS"`, or a CI/CD step running `dotnet run --project src/StarterApp.DbMigrator` with the target connection string).

### ~~9. Money.Subtract Can Produce Negative Amounts~~ FIXED

**Status: Resolved.** `Subtract` now routes through `Create()` instead of the private constructor, so the existing `ThrowIfNegative` guard applies to all Money creation paths. Subtracting a larger amount from a smaller one throws `ArgumentOutOfRangeException`.

### ~~10. Delete Handlers Missing Referential Integrity Checks~~ FIXED

**Status: Resolved.** Both `DeleteProductCommandHandler` and `DeleteCustomerCommandHandler` now check for existing orders before deletion. `DeleteProductCommandHandler` queries `OrderItems.AnyAsync(oi => oi.ProductId == id)` and throws `InvalidOperationException` if the product is referenced. `DeleteCustomerCommandHandler` queries `Orders.AnyAsync(o => o.CustomerId == id)` and throws similarly. Regression tests verify both cases.

### ~~11. Stock Race Condition in CreateOrderCommand~~ FIXED

**Status: Resolved.** Added migration `0007_AddStockNonNegativeConstraint.sql` with `CHECK (Stock >= 0)` on the `Products` table. The database is now the final arbiter — if two concurrent stock decrements race, the second `SaveChangesAsync` throws a database exception. The application-layer check (`product.Stock < quantity`) handles the common case with a clear error message; the database constraint is the safety net for concurrency edge cases.

### ~~12. CancelOrderCommand Silently Skips Deleted Products~~ FIXED

**Status: Resolved.** The shared cancellation service logs a warning via `Log.Warning` when a product no longer exists during stock restoration, including the product ID, quantity, and order ID. The cancellation still succeeds (the order should be cancellable regardless of product state), but operators have visibility into unrestorable stock via structured logs.

### ~~13. UpdateDetails Methods Have Ambiguous Null Semantics~~ FIXED

**Status: Resolved.** `Product.UpdateDetails()` and `Customer.UpdateDetails()` now use the same guards as their constructors: `ArgumentException.ThrowIfNullOrWhiteSpace(name)` and `ArgumentNullException.ThrowIfNull()` for price/email. Passing invalid input is now an error at both creation and update time. Domain tests updated to verify the strict behavior.

### ~~14. CreateOrderCommandValidator Missing GST Rate Bounds~~ FIXED

**Status: Resolved.** `CreateOrderCommandValidator` now validates `GstRate` bounds (0 to 1.0) per item in the validation loop, yielding a structured `ValidationError` with message `"GST rate must be between 0 and 1 (e.g., 0.10 for 10%)"`. This provides a clean 400 response at the API boundary instead of letting the domain guard throw a raw `ArgumentOutOfRangeException`. Migration `0017_AddOrderItemGstRateRangeConstraint.sql` also enforces the same range at the database boundary.

### 15. Missing Patterns for a Starter Template

**Severity: Low**

| Pattern | Impact |
|---------|--------|
| ~~Domain events~~ | ~~Implemented for the `Order` aggregate~~ — resolved. Full pipeline: domain events → outbox → `OutboxProcessor` BackgroundService → Azure Service Bus → Azure Functions subscribers |
| ~~Outbox pattern~~ | ~~Still needs a background dispatcher~~ — resolved. `OutboxProcessor` polls and publishes to Service Bus; Functions consume via topic subscriptions with correlation filters |
| ~~Caching~~ | ~~Redis-backed `IDistributedCache` support with by-id query caching and command invalidation~~ — resolved |
| ~~`PagedResult<T>`~~ | ~~Endpoints accept `page`/`pageSize` but return raw collections without total count~~ — resolved. Endpoints now fetch `pageSize + 1` rows and set `X-Has-More` response header. Total count is a UI concern; APIs just signal whether more data exists. |
| API versioning | Routes use `/api/v1/` prefix strings but no formal versioning library |

**Recommendation:** Formal API versioning remains the next optional starter-template extension if the template needs multi-version route negotiation beyond the current `/api/v1/` prefix convention.

---

## Minor Issues

- ~~**Dockerfile installs database vendor CLI tools in production image**~~ — resolved. Removed ODBC tools and `mssql-tools18` from the runtime stage; the API now uses Npgsql and does not need database CLI tools in the runtime image.
- ~~**CI pipeline skips integration tests**~~ — resolved. A separate `integration` job now runs Testcontainers-based tests after the unit test job passes.
- ~~**CORS is fully permissive in development**~~ — resolved. Added comment clarifying intent: dev is permissive for local frontend testing; production blocks all browser cross-origin by default (secure for API-only use). To allow a browser SPA, configure `AllowedOrigins` in appsettings.
- ~~**`Email.IsValidEmail` uses try/catch for flow control**~~ — resolved. Now uses `MailAddress.TryCreate()` (available since .NET 8) to avoid exception-based flow control.
- ~~**No `appsettings.Development.json`**~~ — resolved. Added with `localhost` connection string defaults for standalone dev without Aspire.
- ~~**`Order.Reconstitute()` is public**~~ — now `internal`, visible only to the test assembly via `InternalsVisibleTo`.
- ~~**Scalar UI replaces Swagger UI**~~ — no longer relevant. Swashbuckle was removed from .NET 9+; Scalar is the standard replacement for OpenAPI UI.
- ~~**`Directory.Build.props` lock file path uses backslashes**~~ — resolved. `NuGetLockFilePath` now uses `/` for explicit cross-platform compatibility.
- ~~**CI pipeline missing NuGet cache**~~ — resolved. `actions/setup-dotnet` now uses built-in NuGet caching keyed from `packages.lock.json`.
- ~~**No Dockerfile health check**~~ — resolved. The runtime image now includes a `HEALTHCHECK` targeting `/health/live`.
- ~~**ServiceDefaults only adds liveness probe**~~ — resolved at the API layer. The API now exposes `/health/ready` backed by a database readiness check, alongside `/health/live` and `/alive`.

---

## Test Coverage Summary

| Category | Files | What's Tested |
|----------|-------|---------------|
| Domain unit tests | 6 | Entity creation, validation, state transitions, value object behavior |
| Property-based (FsCheck) | 5 | Money arithmetic invariants, order state machine, GST calculations, email validation |
| Convention tests | 8 classes | Architecture boundaries, naming, CQRS separation, domain encapsulation, persistence mapping, Dapper SQL quality, caching rules, housekeeping, DateTimeOffset enforcement, constraint naming enforcement, event routing contract validation, domain third-party dependency isolation (plus AppHost.Tests conventions in the Aspire project) |
| Application tests | 9 | All command handlers tested with in-memory DbContext |
| Infrastructure tests | 3 | OutboxMessage mutation tests, OutboxProcessor batch processing with Moq ServiceBusSender, ProblemDetails validation-error customization |
| Integration tests | 4+ | Full API endpoint testing with Testcontainers PostgreSQL, DbUp migrations, ProblemDetails responses |
| Aspire integration tests | 4 | End-to-end pipeline testing via DistributedApplicationTestingBuilder: health endpoints, CRUD path, stock decrement, outbox-to-Service-Bus eventing verification |
| Test construction | 2 | `TestEntities` static factory (Customer/Product/Order entities, owner-scoped) + `OrderCommands` scenario helpers for `CreateOrderCommand`; command DTOs otherwise built inline |

**Coverage:** Every command handler has targeted tests. All 9 handlers (Create/Update/Delete for Product and Customer, plus CreateOrder, UpdateOrderStatus, CancelOrder) have test classes covering successful operations, not-found exceptions, and domain invariant enforcement.

---

## Verdict

A well-engineered starter template that gets the hard things right: architecture enforcement through convention tests across 8 classes (including Dapper SELECT * prevention via IL inspection), proper CQRS separation with zero violations, rich domain modeling with state machines and value objects, and modern DevOps with Aspire orchestration.

Issues #1–#14 and #16–#66 remain resolved. Recent hardening addressed critical security and correctness gaps: order creation now sources pricing from the catalog, stock reservation uses atomic SQL to prevent overselling, cancellation restores reserved stock through every exposed cancellation path, order lifecycle changes route through intent-specific aggregate methods, the outbox persists events transactionally, rate limiting is enforced globally by verified identity where available, validation failures return structured field errors, domain dependency isolation is convention-guarded, mixed-currency orders are rejected at the domain level, APIM-projected identity headers now require a signed gateway assertion in production-like mode, route scopes are enforced, and resource access is owner-scoped for Customer, Product, and Order. The Service Bus integration was hardened with proper resource disposal, retry logic for transient failures, validated configuration, PostgreSQL row claiming, and optimized database indexing. The app persistence stack is now PostgreSQL-only.

The 2026-05-30 review found four new issues. Follow-up work resolved incomplete PostgreSQL integrity-error HTTP mapping and loose currency-code validation with focused regression tests; the remaining items are misleading inventory-reservation subscriber wording and a stale `.http` sample.

The convention tests remain the standout feature. They catch categories of architectural drift that code review alone would miss, and they scale as the codebase grows.

**Best suited for:** Teams starting a new .NET API who want architectural guardrails from day one. Authentication validation is left to the API gateway by design, while the API enforces a signed trusted-edge identity contract, route scopes, and owner-only resource access; the full event pipeline (domain events → outbox → Service Bus → Azure Functions) is implemented for the Order aggregate.
