# Step 3: Container Images and Docker Dependencies

## Overview

This template is Aspire-first. Docker is still required for Aspire-managed dependencies, Testcontainers, and deployable image validation, but the former YAML-based local stack is not a supported run path.

## What Docker Is Used For

- SQL Server, Redis, Azurite, Service Bus emulator, and Seq containers started by `StarterApp.AppHost`
- The Azure Functions runtime container built from [src/StarterApp.Functions/Dockerfile](../../src/StarterApp.Functions/Dockerfile)
- Testcontainers-based integration tests
- Direct image builds for API, DbMigrator, and Functions in CI

## Image Build Validation

From the solution root:

```bash
docker build -f src/StarterApp.Api/Dockerfile .
docker build -f src/StarterApp.DbMigrator/Dockerfile .
docker build -f src/StarterApp.Functions/Dockerfile .
```

The images are intentionally not the local orchestration surface. Use Aspire for full-stack local runs:

```bash
dotnet run --project src/StarterApp.AppHost
```

## Image Responsibilities

| Image | Purpose |
|-------|---------|
| `StarterApp.Api` | Hosts the Minimal API and outbox processor |
| `StarterApp.DbMigrator` | Runs DbUp migrations to completion before API startup in real deployments |
| `StarterApp.Functions` | Hosts Service Bus subscribers under the Azure Functions isolated worker runtime |

## Deployment Notes

- Production-like environments must provide connection strings and gateway assertion settings through the deployment platform.
- Run the DbMigrator to completion before starting API replicas. In container platforms this is usually a job, init container, sidecar dependency, or release pipeline step.
- The API image defaults to normal ASP.NET Core production behavior. OpenAPI/Scalar are exposed only in Development.
- The Functions image currently uses `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0` with `linux/amd64`; on Apple Silicon it runs through Docker emulation when Aspire starts the Functions container.

## Next Step

Continue to [Step 5: Aspire Setup](../05-aspire-setup/README.md) for the supported local orchestration path.
