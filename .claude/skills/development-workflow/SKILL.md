---
name: development-workflow
description: Local stack troubleshooting — Service Bus emulator failures, Aspire/Docker runtime requirements, dev tunnels, running CI locally with act. Use when a container misbehaves, the emulator won't start, or you're changing deployment configuration.
user-invocable: false
---

# Development Workflow

Aspire is the supported local orchestration path. Docker is still required for Aspire dependencies, Testcontainers, the Functions runtime container, and image validation.

## The rules

- **Never use `WithConfigurationFile()` or `WithConfiguration()` for Service Bus topology.** Both fail in ways that look like something else entirely. Use the fluent API. → [reference/service-bus-emulator.md](reference/service-bus-emulator.md)
- **Never assign the 24h TTL constants to emulator topology.** The emulator crash-loops above 1 hour; run mode clamps via `ServiceBusTopology.ClampForEmulator`, publish mode keeps the deployed posture.
- **Every DI registration must work with no Aspire service discovery present.** Use conditional registration or fallback defaults — CI builds all three images with plain `docker build`, and integration tests run without Service Bus.
- **Connection strings standardize on the `database` key.** Aspire injects it locally; container deployments must supply it.
- **`MapHealthChecks("/health")` is mapped unconditionally** — container platforms probe outside Development.
- **Never introduce a Microsoft apt repo or GPG key import into a Dockerfile.** The runtime comes from digest-pinned `mcr.microsoft.com` base images. Resolve a new digest with `docker buildx imagetools inspect <image>`; a mutable tag defeats both the pin and the Dependabot updater.
- **Never override `PATH` in `.act.env`.** The catthehacker runner image already has the right Node on `PATH`, and pinning a patch version breaks on every image bump.

## Commands

```bash
act                                                   # run CI locally (.actrc pins arch + image)
act -j build

./scripts/smoke-test.sh https://localhost:7286        # post-deploy check; non-zero exit on failure
SMOKE_BASE_URL=https://staging.example.com ./scripts/smoke-test.sh

DEV_TUNNEL_ACK_UNSIGNED_API=true \
  dotnet run --project src/StarterApp.AppHost -- --devtunnel
```

The dev-tunnel acknowledgment is a real gate, not ceremony: the tunneled API runs `GatewayIdentity:Mode=UnsignedDevelopment`, which trusts identity headers with no signed assertion, so AppHost refuses to start the tunnel without it.

## Depth

| Topic | Reference |
|---|---|
| Emulator failure modes, triage order, OOM vs TTL, recovery | [reference/service-bus-emulator.md](reference/service-bus-emulator.md) |
| Dockerfile layout, act configuration, smoke-test coverage, dev tunnel setup | [reference/docker-and-ci.md](reference/docker-and-ci.md) |

## Related skills

- `testing-strategy` — the test projects these containers back
- `data-access` — Aspire wiring and migration execution
