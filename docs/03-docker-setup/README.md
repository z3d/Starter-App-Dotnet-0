# Step 3: Docker Setup and Containerization

## Overview

Containerizing the .NET Web API, Azure Functions, SQL Server, Redis, Service Bus emulator, and Seq with Docker Compose for a production-like environment.

## Current Status

✅ **Already Configured!** - Docker setup includes:

- **Multi-stage Dockerfiles** for the API, DbMigrator, and Functions
- **Docker Compose** orchestration for API + Functions + SQL Server + Redis + Azurite + Service Bus emulator + Seq
- **Seq centralized logging** with web interface
- **Health checks** for service monitoring
- **Volume persistence** for database, log, cache, and payload archive storage
- **Environment-specific configuration**

> **⚠️ Apple Silicon note**: The Azure Functions base image
> `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`
> is only published for `linux/amd64`. On arm64 Macs, either run the
> stack without the `functions` service, or use Aspire
> (`dotnet run --project src/StarterApp.AppHost`) which runs Functions
> natively via the Azure Functions Core Tools.

## Docker Architecture

```
Docker Environment
├── api                 (StarterApp.Api, .NET 10)
├── migrator            (StarterApp.DbMigrator — runs to completion)
├── functions           (StarterApp.Functions, Service Bus subscribers)
├── db                  (SQL Server 2022 — main application DB)
├── sqledge             (Azure SQL Edge — backing store for the SB emulator)
├── azurite            (Azure Blob/Queue/Table storage emulator for payload archive + Functions host storage)
├── servicebus-emulator (Azure Service Bus emulator)
├── redis               (Redis 7 — distributed cache)
├── seq                 (Seq — centralized log aggregation)
└── backend-network     (bridge)
```

## Files Overview

### 1. Dockerfiles

| File | Purpose |
|------|---------|
| [src/StarterApp.Api/Dockerfile](../../src/StarterApp.Api/Dockerfile) | Multi-stage build for the Web API |
| [src/StarterApp.DbMigrator/Dockerfile](../../src/StarterApp.DbMigrator/Dockerfile) | Multi-stage build for the migration runner |
| [src/StarterApp.Functions/Dockerfile](../../src/StarterApp.Functions/Dockerfile) | Multi-stage build for Azure Functions (isolated worker) |

**Multi-stage build pattern** (shared across all three):
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# restore + publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# copy published output, set entry point
```

### 2. Docker Compose ([docker-compose.yml](../../docker-compose.yml))

**Services:**
- **migrator** - Runs DbUp migrations, exits 0 on success
- **api** - The Web API (depends on migrator completing + db/redis/seq/servicebus-emulator healthy)
- **functions** - Azure Functions worker (Service Bus subscribers)
- **db** - SQL Server 2022 (application DB)
- **sqledge** - Azure SQL Edge (backs the Service Bus emulator)
- **azurite** - Azure Storage emulator for payload archive/audit artifacts and Azure Functions host storage
- **servicebus-emulator** - Azure Service Bus emulator (topology in [config/servicebus-emulator.json](../../config/servicebus-emulator.json))
- **redis** - Redis 7 (distributed cache for queries implementing `ICacheable`)
- **seq** - Centralized structured log viewer

## Running with Docker

### Quick Start
```bash
# From the solution root
docker compose up --build
```

On Apple Silicon, exclude Functions:
```bash
docker compose up --build migrator api db redis seq servicebus-emulator sqledge
```

### Step-by-Step

1. **Build the images:**
   ```bash
   docker compose build
   ```

2. **Start the services:**
   ```bash
   docker compose up -d
   ```

3. **View logs:**
   ```bash
   docker compose logs -f             # all services
   docker compose logs -f api         # single service
   ```

4. **Check service health:**
   ```bash
   docker compose ps
   ```

## Service Endpoints

| Service | URL | Description |
|---------|-----|-------------|
| **API** | http://localhost:8080 | Main API endpoint |
| **Scalar API Reference** | http://localhost:8080/scalar/v1 | API documentation |
| **Health (aggregate)** | http://localhost:8080/health | Overall service health |
| **Readiness** | http://localhost:8080/health/ready | Traffic-ready probe (includes DB) |
| **Liveness** | http://localhost:8080/health/live | Container liveness probe |
| **Seq Logs** | http://localhost:5341 | Centralized log viewer |
| **SQL Server** | localhost:1433 | Database connection |
| **Redis** | localhost:6379 | Redis cache |
| **Azurite Blob** | http://localhost:10000 | Payload archive/audit Blob endpoint |
| **Service Bus Emulator** | localhost:5672 | AMQP endpoint |

## Configuration Details

### API Environment Variables
```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Docker
  - ConnectionStrings__database=Server=db;Database=StarterApp;User Id=sa;Password=Your_password123;TrustServerCertificate=True;
  - ConnectionStrings__redis=redis:6379
  - ConnectionStrings__servicebus=Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
  - ConnectionStrings__payloadarchive=UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://azurite
  - PayloadCapture__RequireArchiveStore=true
  - PayloadCapture__FailureMode=FailClosed
  - SEQ_URL=http://seq:5341
```

### Database Container
```yaml
environment:
  - ACCEPT_EULA=Y
  - SA_PASSWORD=Your_password123
  - MSSQL_PID=Developer
```

### Seq Container
```yaml
environment:
  - ACCEPT_EULA=Y
  - SEQ_FIRSTRUN_NOAUTHENTICATION=true
ports:
  - "5341:80"
volumes:
  - seq-data:/data
```

### Health Checks

**API:**
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 10s
```

**SQL Server:**
```yaml
healthcheck:
  test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Your_password123" -C -Q "SELECT 1" -b
  interval: 10s
  timeout: 5s
  retries: 5
  start_period: 30s
```

## Data Persistence

### Volumes
```yaml
volumes:
  sqldata:    { driver: local }
  redis-data: { driver: local }
  seq-data:   { driver: local }
  azurite-data: { driver: local }
```

Docker Compose intentionally mirrors Aspire-owned dependencies. Payload archive/audit writes use the Azurite-backed `payloadarchive` connection string, API and Functions wait for Azurite health, and both services set `PayloadCapture__RequireArchiveStore=true` with `FailClosed` so storage drift fails loudly instead of silently dropping support artifacts.

### Volume Management
```bash
# List volumes
docker volume ls

# Inspect volume
docker volume inspect <project>_sqldata

# Backup volume (example)
docker run --rm -v <project>_sqldata:/data -v "$PWD:/backup" ubuntu \
  tar czf /backup/sqldata-backup.tar.gz -C /data .
```

## Networking

All services share the `backend-network` bridge. Containers reach each other by service name (e.g. the API uses `db`, `redis`, `seq`, `servicebus-emulator` as hostnames).

## Database Migrations

Migrations run in a dedicated `migrator` service, not embedded in API startup. The API waits for `migrator` to complete successfully before starting:

```yaml
api:
  depends_on:
    migrator:
      condition: service_completed_successfully
```

This eliminates race conditions when multiple API replicas start simultaneously.

### Running Migrations Manually

```bash
# Run only the migrator (useful after adding a new script)
docker compose run --rm migrator
```

## Docker Commands Reference

### Basic Operations
```bash
docker compose up --build       # build and start
docker compose up -d            # start detached
docker compose down             # stop services
docker compose down -v          # stop and remove volumes (⚠ loses data)
docker compose logs -f          # tail logs
```

### Development Commands
```bash
docker compose build api        # rebuild specific service
docker compose restart api      # restart specific service
docker compose exec api bash    # shell into a running container
docker compose top              # list processes
```

### Cleanup
```bash
docker compose down             # containers + network
docker compose down -v          # + volumes (⚠ loses data)
docker system prune             # unused images/containers/networks
docker container prune          # all stopped containers
```

## Troubleshooting

### Functions image fails to pull on arm64
```
no match for platform in manifest: not found
```
The `dotnet-isolated:4-dotnet-isolated10.0` tag is amd64-only. See the note at the top of this doc — use Aspire or exclude Functions.

### Port already in use
```bash
lsof -i :8080     # macOS/Linux
# Stop the process or change the host port in docker-compose.yml
```

### Container won't start
```bash
docker compose logs api
docker compose ps
```

### Database connection issues
```bash
# Verify SQL Server is ready
docker compose exec db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Your_password123" -C -Q "SELECT @@VERSION"

# Network connectivity
docker compose exec api ping db
```

### Build failures
```bash
docker compose down
docker compose build --no-cache
docker compose up
```

## Production Considerations

- Use secrets management (Key Vault, Secrets Manager) instead of env vars in compose
- Pin image tags (avoid `:latest`)
- Run containers as non-root users
- Configure CPU/memory limits
- Scan images for vulnerabilities
- Aggregate logs to a managed service (the Seq container here is for local dev)

## Next Step

Continue to **[Step 5: Aspire Setup](../05-aspire-setup/README.md)** for .NET Aspire orchestration.
