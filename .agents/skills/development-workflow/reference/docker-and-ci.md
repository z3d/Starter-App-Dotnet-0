# Docker, Local CI, and Deployment Checks

## Dockerfiles

The three Dockerfiles (`src/StarterApp.Api`, `src/StarterApp.DbMigrator`, `src/StarterApp.Functions`) pull the SDK and runtime from **digest-pinned `mcr.microsoft.com` images** — `dotnet/sdk:10.0@sha256:…` for build, `dotnet/aspnet:10.0@sha256:…` or `azure-functions/dotnet-isolated:…@sha256:…` for the final stage.

There is **no** Microsoft apt repo, GPG key import (`gpg --dearmor` / `signed-by=`), or `prod.list` to maintain. The only apt step in the final stage installs `curl` for the healthcheck via plain `apt-get install -y --no-install-recommends curl`, then clears `/var/lib/apt/lists/*`. Do not introduce a package-repo block — the runtime comes from the base image.

Resolve a new digest with:

```bash
docker buildx imagetools inspect <image>
```

A mutable tag would defeat both the digest pin and the Dependabot docker updater, which cannot bump a bare tag.

Build stages COPY `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `NuGet.config`, and the per-project csproj + `packages.lock.json` files, then run `dotnet restore --locked-mode`, so images ship exactly the audited dependency closure. **Never add those files to `.dockerignore`** — the build silently loses its pinned closure. Docker does not honour `.gitignore`, so `.dockerignore` is what keeps `bin/`, `obj/`, dev config, secrets, docs, and test projects out of the context.

## Aspire-first runtime requirements

Aspire is the supported local orchestration path, but every path must also work without it:

- **Connection strings** standardize on the `database` key. Aspire injects it for local orchestration; real container deployments must provide it explicitly.
- **Health checks** — `app.MapHealthChecks("/health")` is mapped unconditionally, because container platforms probe readiness and liveness outside Development.
- **Service registration** — all DI registrations must work with no Aspire service discovery present. Use conditional checks or fallback defaults; Service Bus and Redis both no-op without their connection strings for exactly this reason.
- **Image validation** — CI builds the API, DbMigrator, and Functions images with direct `docker build -f … .` commands, so those paths must stay buildable outside Aspire.

## Local CI with act

Runs GitHub Actions workflows locally in Docker before pushing. Install per <https://nektosact.com/installation/> (`brew install act`, `winget install nektos.act`, or the install script); Docker must be running.

```bash
act              # run the CI workflow
act -j build     # a specific job
act -v           # verbose
act --list       # available workflows
```

`.actrc` sets `--container-architecture linux/amd64`, the runner image (`-P ubuntu-latest=catthehacker/ubuntu:act-latest`), and `--env-file .act.env`.

`.act.env` sets `TESTCONTAINERS_RYUK_DISABLED=true` so Testcontainers-backed tests run under act's nested Docker — the Ryuk reaper cannot be reached across the nested-Docker boundary.

It deliberately does **not** override `PATH`. The catthehacker runner image already puts the correct bundled Node on `PATH`, and pinning a patch version there breaks on every image bump.

## Dev tunnels

Exposes the local API to the internet for webhook testing, mobile development, or sharing. Uses `Aspire.Hosting.DevTunnels`.

Prerequisites — the [Dev Tunnel CLI](https://aka.ms/devtunnels/docs) and a one-time login:

```bash
brew install --cask devtunnel                          # macOS
winget install Microsoft.devtunnel                     # Windows
curl -sL https://aka.ms/DevTunnelCliInstall | bash     # Linux
devtunnel user login
```

Usage requires an explicit security acknowledgment:

```bash
DEV_TUNNEL_ACK_DEV_IDP=true dotnet run --project src/StarterApp.AppHost -- --devtunnel
# or
ENABLE_DEV_TUNNEL=true DEV_TUNNEL_ACK_DEV_IDP=true dotnet run --project src/StarterApp.AppHost
```

The acknowledgment exists because the tunneled API accepts tokens from the local dev Keycloak, whose realm ships well-known development credentials — anyone who can reach the tunnel can mint a valid token. AppHost refuses to start the tunnel unless you acknowledge exposing that surface.

## Smoke testing a deployment

```bash
./scripts/smoke-test.sh https://localhost:7286        # Aspire; API URL from the dashboard
SMOKE_BASE_URL=https://localhost:7286 ./scripts/smoke-test.sh
```

`BASE_URL` is required — positionally or via `SMOKE_BASE_URL`. With neither set the script prints usage and exits 2; there is deliberately no baked-in default.

Roughly 25 assertions covering: health check (warn-only, since Aspire health probes can fail when called externally), CRUD across products/customers/orders, every validator rule (email format and length, currency, OrderId, status enum), conflict responses (invalid state transitions, referential integrity), not-found responses, and the order lifecycle create → confirm → cancel.

Design choices worth preserving: `curl` only so it runs anywhere with zero dependencies; unique test data per run via a timestamp suffix, so it is idempotent and needs no cleanup; non-zero exit on failure so it works as a post-deploy CI gate; HTTPS auto-detected with cert verification skipped for dev certs.

It complements integration tests rather than duplicating them — integration tests verify correctness in-process, the smoke test verifies the deployed artifact actually works.
