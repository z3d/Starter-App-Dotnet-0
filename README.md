# Starter App Project with .NET Aspire

A production-grade template for a .NET 10 service: Clean Architecture, CQRS and DDD over PostgreSQL, orchestrated locally by .NET Aspire, with domain events published through a transactional outbox to Azure Service Bus and consumed by Azure Functions, and a React web app that signs in with OIDC and calls the API through a generated client. Licensed under the GNU AGPL v3 only (`AGPL-3.0-only`); see [LICENSE](LICENSE).

It is maintained by AI agents, so it favours mechanical rules over architectural taste. The convention tests under `tests/StarterApp.Tests/Conventions/` are the authority on structure; [AGENTS.md](AGENTS.md) holds the rules they cannot enforce; [docs/DECISIONS.md](docs/DECISIONS.md) records why each notable choice was made and what would justify revisiting it. A project derived from this template prunes the sample domain and every capability its first module does not use ([docs/DERIVATION-PRUNING.md](docs/DERIVATION-PRUNING.md)).

## Run it

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), a running Docker-compatible container engine ([Docker Desktop](https://www.docker.com/products/docker-desktop) or [Podman](https://podman.io/); Aspire-managed dependencies, the Functions runtime container, Testcontainers and image builds all talk to its socket, and only `tests/k6/run-perf.sh` needs the `docker` CLI itself), and Node 24 with npm for the web app. With Podman, `podman machine start` is the whole fix for a `DockerUnavailable` test failure.

```bash
dotnet run --project dev/StarterApp.AppHost        # the full stack; the dashboard opens at https://localhost:17113
cd web && npm install && npm run dev               # the web app alone at http://localhost:5173 (the AppHost starts it too)
dotnet test                                        # everything; integration and Aspire tests need the container engine
dotnet test --filter "FullyQualifiedName!~Integration"   # unit, convention and fuzz tests only
```

The AppHost boots a dev Keycloak with the committed `starterapp` realm and points the API's `Identity:Authority` at it, so local development exercises the exact production path: OIDC discovery, a JWKS fetch and RS256 verification inside the API. There is no unsigned or bypass mode in any environment. The web app signs you in with the authorization code flow and PKCE, shows the claims the API will verify, then drives product, customer and order through the generated client. Sign in again without `orders:write` to watch the API refuse the order from the token's scopes alone, or as `password-only` to watch a write answer the step-up challenge and send you back for the second factor. The dev users and the `starterapp-dev` client are committed, well-known credentials under `dev/StarterApp.AppHost/Realms/`.

The sample domain is owner-scoped end to end: products, customers and orders belong to the tenant and subject that created them, every read filters by that owner and every write checks it, so there is no shared catalogue. That is deliberate; the sample exists to demonstrate owner scoping, not to model a store.

[docs/GETTING-STARTED.md](docs/GETTING-STARTED.md) covers running the API alone, migrations, minting a dev token, the image builds and publishing to Azure. [web/README.md](web/README.md) covers the web workspace.

## Where the rules live

| Question | Source of truth |
|---|---|
| Is this structure allowed? | `tests/StarterApp.Tests/Conventions/`, one test file per concern |
| What can the compiler and the tests not catch? | [AGENTS.md](AGENTS.md), with the traps that cost a session and the recorded decisions' re-add triggers |
| Why was it done this way? | [docs/DECISIONS.md](docs/DECISIONS.md) |
| How do I write a handler, an entity, an endpoint, a test? | `.claude/skills/`, with pinned exemplars under `docs/exemplars/` |
| What is still open? | [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md), with dated records under `docs/reviews/` |

## What it demonstrates

**Identity.** Zero-trust OIDC/JWT: the API validates every bearer token itself against the configured authority, with cached discovery and JWKS and no introspection calls. Route metadata enforces identity, scope and MFA step-up (RFC 9470) before dispatch; owner scoping is checked in query predicates and command handlers. Rate limiting is partitioned by the verified tenant and subject. The web app is the client half of that seam: a public client with PKCE, the token on every call, a generated OpenAPI client that CI fails when it is stale, and an nginx image that renders the identity provider from the environment at start-up.

**Data.** EF Core for commands, Dapper for queries, `xmin` concurrency tokens, Redis caching of by-id queries only (a cached list page would be visibly stale). Migrations run only through `StarterApp.DbMigrator`, never at API start-up, and from script 0010 each is an Expand or a Contract so the previous build keeps serving through a rollout. A connection string with no password means the hosting identity's Entra token, everywhere; a raw `new NpgsqlConnection` is a build error. Order creation takes an `Idempotency-Key`. Nothing reads the clock except through `TimeProvider`.

**Eventing.** Domain events are the wire contract: raised inside aggregates, captured into the outbox in the same save, published to Azure Service Bus by a background processor and consumed by Azure Functions through topic subscriptions, each delivery processed once through a database inbox. The migrator's `replay-outbox` verb and [docs/runbooks/event-replay.md](docs/runbooks/event-replay.md) handle a stuck or dead-lettered event. A job watch judges every timer-triggered function against its own schedule and logs what needs attention ([docs/runbooks/scheduled-jobs.md](docs/runbooks/scheduled-jobs.md)). The Service Bus emulator runs locally under Aspire.

**Payload archive and audit.** Bounded HTTP payload capture and full Service Bus payload archiving to Blob storage, as the first middleware so rejected traffic is still seen, with an explicit fail-open or fail-closed policy per channel and an entity index for breach scoping.

**Operations.** Liveness and readiness probes plus durable dependency checks for the database, cache, Service Bus and archive store; distributed tracing, metrics and structured logging through Aspire's service defaults; feature toggles that fail closed; a smoke test for a live deployment; read-only support SQL under `scripts/reporting/`; recurring async-failure patterns under `docs/investigations/`.

**Security and supply chain.** Chiseled, digest-pinned, non-root container images for the API, migrator, Functions and web app, built and Trivy-scanned in CI and published to GHCR after a green run; CodeQL; full-history secret scanning with a pinned gitleaks; an OWASP ZAP DAST run with a cross-owner IDOR probe; a nightly k6 gate against a seeded volume; locked-mode restore with central package versions, a restricted feed and a pinned SDK; pinned GitHub Actions. When published, every dependency is reached with a managed identity and nothing is given a key.

**Testing.** Unit, convention, property-based (FsCheck), fuzz and Testcontainers integration tests in one project; Aspire end-to-end tests opted into with `STARTERAPP_ASPIRE_TESTS=true`; Vitest for the web helpers and Playwright against a stack `web/e2e/run-e2e.sh` boots itself.

## Project structure

```
starterapp/
├── src/                             # Only what ships
│   ├── StarterApp.Api/              # Minimal API, mediator, outbox processor, payload capture
│   ├── StarterApp.SharedKernel/     # AggregateRoot, IDomainEvent, domain exceptions, Email and Money
│   ├── StarterApp.Domain/           # The Customer/Product/Order sample: aggregates and domain events
│   ├── StarterApp.Functions/        # Service Bus subscribers, archive cleanup, job watch
│   ├── StarterApp.DbMigrator/       # DbUp migrations and the replay-outbox verb
│   └── StarterApp.ServiceDefaults/  # Shared Aspire wiring, database authentication, job-run recording
├── dev/
│   └── StarterApp.AppHost/          # .NET Aspire orchestration, the dev Keycloak realm, the Azure publish shape
├── web/                             # The web app (Vite, React), its generated API client and the browser tests (npm)
├── tests/
│   ├── StarterApp.Tests/            # Unit, convention, integration, property and fuzz tests
│   ├── StarterApp.AppHost.Tests/    # Aspire end-to-end tests
│   └── k6/                          # Performance gate
├── dast/                            # OWASP ZAP scan and its seed
├── docs/                            # Getting started, API reference, decisions, derivation pruning, runbooks, investigations, exemplars, review records
├── scripts/                         # Smoke test, dev scripts, reporting SQL
└── .claude/                         # Skills, path-scoped rules, reviewer agents, hooks and the review workflow for the agents that maintain this repository
```

## Deriving a project from this template

Copy the repository, rename the solution, then follow [docs/DERIVATION-PRUNING.md](docs/DERIVATION-PRUNING.md): at its first module a derived project removes the sample domain and every capability no module uses, and `DerivationConventionTests` fails while any of it remains. Every seam that survives there is a real boundary with a module consumer.

## Working on it

Every change is formatted, built and tested before it is committed, and every agent commit carries `Harness:` and `Model:` trailers, enforced by `.githooks/commit-msg`. Several agents may share one checkout, so work happens in a worktree that ends merged or abandoned; the hooks under `.claude/hooks/` say when one is parked. [AGENTS.md](AGENTS.md) has the commands and the rules.

CI runs locally with [act](https://github.com/nektos/act): plain `act` uses the flags in `.actrc`; run a targeted job when the local engine cannot carry the whole workflow.

## Documentation

- **[Getting started](docs/GETTING-STARTED.md)**: prerequisites, running the stack, migrations, tests, image builds and publishing to Azure
- **[API endpoints](docs/API-ENDPOINTS.md)**: every Minimal API endpoint with examples and how to mint a dev token
- **[Rules of the codebase](AGENTS.md)**: what agents and people must follow, and where each subsystem is documented
- **[Recorded decisions](docs/DECISIONS.md)**: why each notable choice was made and what would justify revisiting it
- **[Derivation pruning](docs/DERIVATION-PRUNING.md)**: turning this template into a project
- **[Architecture review](docs/ARCHITECTURE_REVIEW.md)**: open findings and accepted limitations, with dated records under `docs/reviews/`
- **[Runbooks](docs/runbooks/)**: replaying an event, a scheduled job that failed or did not run
- **[The web app](web/README.md)**: the workspace, the generated client, the browser tests and the web image
