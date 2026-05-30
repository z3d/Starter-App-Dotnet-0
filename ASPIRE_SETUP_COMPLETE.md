# .NET Aspire Setup Complete

This project is now Aspire-first: `StarterApp.AppHost` is the supported local orchestration path, while Docker remains the runtime for Aspire-managed infrastructure containers, the Functions worker image, Testcontainers, and image build validation.

## What's Included

- `StarterApp.AppHost` orchestrates the API, DbMigrator, Functions container, PostgreSQL, Redis, Azurite, Service Bus emulator, and Seq.
- `StarterApp.ServiceDefaults` provides shared observability and service configuration.
- The API uses health checks, OpenTelemetry, structured logging, Redis caching, Service Bus publishing, and payload capture.
- The Functions worker runs through the Azure Functions Docker runtime so local subscriber behavior matches the deployed worker shape.

## Quick Start

```bash
dotnet run --project src/StarterApp.AppHost
```

The Aspire dashboard URL is printed in the console and opens automatically when launched from the configured profile.

## Container Image Validation

The former YAML-based local stack is not a supported run path. Validate deployable images directly:

```bash
docker build -f src/StarterApp.Api/Dockerfile .
docker build -f src/StarterApp.DbMigrator/Dockerfile .
docker build -f src/StarterApp.Functions/Dockerfile .
```

> **Apple Silicon note**: The Azure Functions base image `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0` is currently published for `linux/amd64` only. Aspire can still start it through Docker emulation, but it may be slower on arm64 Macs.

## What the Dashboard Shows

- **Services**: API, PostgreSQL, Redis, Blob storage emulator, Service Bus emulator, Functions container, DbMigrator, Seq
- **Traces**: HTTP requests, database queries, Service Bus publishes, and subscriber activity
- **Metrics**: Request duration, error rates, throughput, and runtime metrics
- **Logs**: Structured logs with correlation IDs
- **Resources**: Container health and endpoint links

## Next Steps

1. Start the stack with `dotnet run --project src/StarterApp.AppHost`.
2. Open the dashboard and inspect resource logs/traces.
3. Run `dotnet test` for unit, convention, integration, and Aspire coverage.
4. Use direct `docker build -f ... .` commands or the CI `docker-build` job to validate images.

## Documentation

- [Aspire Setup](docs/05-aspire-setup/README.md)
- [Container Images and Docker Dependencies](docs/03-docker-setup/README.md)
- [API Endpoints](docs/API-ENDPOINTS.md)
