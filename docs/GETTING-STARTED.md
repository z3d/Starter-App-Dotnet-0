# Getting Started

How to run this template locally for the first time. For the rules of the codebase read
`CLAUDE.md`; for endpoint details and how to mint a dev token read [API-ENDPOINTS.md](API-ENDPOINTS.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Desktop (or another Docker engine). Aspire starts every dependency as a container, and
  the integration tests use Testcontainers.
- Optional: [`act`](https://github.com/nektos/act) to run the GitHub Actions workflow locally
  (flags are in `.actrc`).

## Run the whole stack

```bash
dotnet run --project dev/StarterApp.AppHost
```

Aspire is the only supported local orchestration path. There is deliberately no compose file. The
AppHost starts, in dependency order:

| Resource | Role |
|---|---|
| PostgreSQL | Persistence. The `migrator` project runs the DbUp scripts first; the API waits for it to complete. |
| Redis | Distributed cache for by-id queries |
| Azure Storage emulator | Payload archive, audit, and entity-index blobs |
| Azure Service Bus emulator | `domain-events` topic with the `email-notifications` and `inventory-reservation` subscriptions |
| Keycloak | Dev OIDC realm with asymmetric signing, imported from `dev/StarterApp.AppHost/Realms/` |
| Functions container | `StarterApp.Functions` Service Bus subscribers in the Azure Functions isolated-worker image |
| Seq | Structured log sink |
| `api` | The Minimal API plus the outbox processor |

The Aspire dashboard URL is printed on start-up (the port is dynamic). It shows every resource's
health, logs, traces, and endpoints. The Scalar API reference is at `/scalar` on the API
endpoint in Development.

The Functions image is published for `linux/amd64`; on Apple Silicon it runs under emulation and
starts slowly. If the Service Bus emulator crash-loops or a container is stuck, follow
`.claude/skills/development-workflow/SKILL.md`.

Pass `--devtunnel` (or set `ENABLE_DEV_TUNNEL=true`) to expose the API through a dev tunnel for
webhook or mobile testing. The tunnel is never registered unless explicitly enabled.

## Run the API alone

```bash
dotnet run --project src/StarterApp.Api
```

Standalone runs fall back to the connection strings in `appsettings.Development.json` (copy the
tracked `.example` file; the real file is git-ignored). You must run migrations yourself first and
supply a PostgreSQL, Redis, and OIDC authority the API can reach. `scripts/dev/keycloak.sh` starts
(or reuses) the same dev Keycloak the AppHost runs, on `:8090` with the committed realm imported;
point `Identity:Authority` at `http://localhost:8090/realms/starterapp` with
`Identity:RequireHttpsMetadata=false`, and `scripts/dev/keycloak.sh stop` removes it. Service Bus is optional in
Development: with no `ConnectionStrings:servicebus` the publisher is a no-op. Outside
Development and Testing a missing Service Bus connection string fails start-up on purpose.

A `ConnectionStrings:database` that names a `Username` but no `Password` makes the API connect
to Azure Database for PostgreSQL as its hosting identity: `DatabaseAuthentication` fetches an
Entra token through `DefaultAzureCredential` and Npgsql presents it as the password, refreshing
it before expiry. The start-up log names the mode it chose. The migrator resolves the same token
once up front (`DatabaseAuthentication.ResolveForDirectUseAsync`) and finishes inside its lifetime.

## Migrations

Migrations run only through `StarterApp.DbMigrator`, never at API start-up. Under Aspire that
happens automatically. Standalone, the migrator reads the same `ConnectionStrings:database` key as
the API, from configuration or the command line:

```bash
dotnet run --project src/StarterApp.DbMigrator -- --ConnectionStrings:database "<postgres-connection-string>"
```

Real deployments run the migrator to completion (a job, init container, or release step) before
starting API replicas. To add a migration, add the next numbered `.sql` file under
`src/StarterApp.DbMigrator/Scripts/` with explicit deterministic names for every constraint and
index, then run the tests; the migration conventions check embedding and naming. Details in
`.claude/skills/data-access/SKILL.md`.

## Calling the API

All `/api/v1` routes require an OIDC bearer token the API validates itself. Health and OpenAPI
endpoints are public. Mint a token from the dev Keycloak realm with the command in
[API-ENDPOINTS.md](API-ENDPOINTS.md); write routes additionally need the matching `*:write`
scope and `mfa` in the `amr` claim, which the dev realm stamps.

## Tests

```bash
dotnet test                                                                # everything; needs Docker
dotnet test --filter "FullyQualifiedName!~Integration"                  # unit + convention + fuzz; its DB-backed handler tests still need Docker
STARTERAPP_ASPIRE_TESTS=true dotnet test tests/StarterApp.AppHost.Tests  # Aspire end-to-end
```

Aspire end-to-end tests live in `tests/StarterApp.AppHost.Tests` and boot the full distributed app
once per collection. They are `[AspireFact]`s: skipped, and reported as skipped, unless
`STARTERAPP_ASPIRE_TESTS=true` is set. Which project a new test belongs in is covered by
`.claude/skills/testing-strategy/SKILL.md`.

## Container images

Aspire uses the Functions Dockerfile directly; the other images exist for deployment and are
validated in CI. To build them locally from the repo root:

```bash
docker build -f src/StarterApp.Api/Dockerfile .
docker build -f src/StarterApp.DbMigrator/Dockerfile .
docker build -f src/StarterApp.Functions/Dockerfile .
```

Production-like environments supply connection strings and the `Identity:Authority` and
`Identity:Audience` settings through the platform. OpenAPI and Scalar are exposed only in
Development.

## Publishing to Azure

The AppHost describes the Azure shape in publish mode and nothing else in this repository does:

| Resource | Locally | Published |
|---|---|---|
| `postgres` | container (password) | Azure Database for PostgreSQL, Entra-only; each app's managed identity is the user |
| `storage`, `servicebus` | emulators (keyed connection strings) | the Azure services, endpoints + managed identity |
| `redis` | container | Azure Managed Redis (Balanced B0), Entra auth only — access keys disabled |
| `seq` | container | not published; logs and traces reach the Aspire dashboard and Log Analytics over OTLP |
| `keycloak` | container with `Realms/` bind-mounted, `admin`/`admin` | the same image with the realm copied in (`Realms/Dockerfile`); admin password is a generated secret parameter |
| `migrator` | project, run once | a Container App **job** the deployer starts after each deploy |
| `api`, `functions` | as today | container apps in one Container Apps environment, each with its own managed identity |

Every deployed dependency is reached with the app's managed identity: there is no key, password or
connection string anywhere in the environment (Postgres has password authentication disabled, Redis has
access keys disabled). The one exception is the Keycloak *realm*, whose well-known dev users are the point.

The `azure.yaml`, environment values and ingress restrictions belong to the hosting environment's
repository (`docs/DECISIONS.md`, "Production infrastructure as code"); run `azd` from there, never from here.
To see exactly what would be published: `dotnet run --project dev/StarterApp.AppHost -- --publisher manifest --output-path /tmp/manifest/manifest.json`.

## Troubleshooting

- **API never becomes healthy.** Check the migrator resource in the dashboard first; the API waits
  for it. Then confirm PostgreSQL is healthy.
- **Service Bus emulator not ready or exit code 139.** The emulator rejects any TTL over one hour;
  run mode clamps for it. See the development-workflow skill for the reset script.
- **Tests fail with `DockerUnavailableException`.** The integration and DB-backed handler tests
  need a running Docker engine.
- **Wrong `dotnet` picked up.** If a globally installed SDK shadows the pinned one in
  `global.json`, check `dotnet --version` from the repo root.
