# Step 5: .NET Aspire Setup - Modern Cloud-Native Orchestration

## Overview

.NET Aspire provides a superior local development experience with built-in observability, service discovery, and configuration management. This step shows how Aspire complements the Docker setup for cloud-native development.

## Current Status

✅ **Already Configured!** - .NET Aspire is fully set up with:

- **AppHost Project** (`StarterApp.AppHost`) - Orchestrates all services
- **Service Defaults** (`StarterApp.ServiceDefaults`) - Shared configuration and observability
- **SQL Server Integration** - Containerized DB with automatic setup and migration
- **Redis Integration** - Distributed cache
- **Azure Service Bus Emulator** - Domain-event messaging with topic + subscriptions
- **Azure Functions** - Service Bus subscribers running natively via Functions Core Tools
- **Seq** - Centralized structured logging
- **DevTunnel (optional)** - Exposes the API to the internet for webhook/mobile testing
- **Observability Dashboard** - Built-in monitoring and diagnostics

## 🏗️ Aspire Architecture

```
StarterApp.AppHost (Orchestrator)
├── 📊 Aspire Dashboard (Built-in observability)
├── 🌐 StarterApp.Api (Enhanced with telemetry)
├── 🔄 StarterApp.DbMigrator (Runs DbUp migrations; API waits for completion)
├── ⚡ StarterApp.Functions (Service Bus subscribers)
├── 🗄️ SQL Server container (Persistent lifetime)
├── 🚀 Redis container (Distributed cache)
├── 📬 Azure Service Bus emulator (Topic: domain-events with 2 subscriptions)
├── 📝 Seq container (Centralized logs)
└── 📈 Service Defaults (Shared configuration)
```

## 🆚 Development Approaches Comparison

| Aspect | Docker Compose | .NET Aspire | Winner |
|--------|----------------|-------------|--------|
| **Configuration** | YAML | C# with IntelliSense | 🥇 Aspire |
| **Service Discovery** | Manual networking | Automatic resolution | 🥇 Aspire |
| **Observability** | Requires setup | Built-in dashboard | 🥇 Aspire |
| **Debugging** | Attach to containers | Native .NET debugging | 🥇 Aspire |
| **Hot Reload** | Container rebuilds | Instant .NET hot reload | 🥇 Aspire |
| **Production Parity** | High | Medium | 🥇 Docker |
| **CI/CD Integration** | Excellent | Good | 🥇 Docker |
| **Apple Silicon support** | Functions image is amd64-only | Functions runs natively | 🥇 Aspire |

## 🚀 Quick Start with Aspire

### 1. Start the Application

```bash
dotnet run --project src/StarterApp.AppHost
```

Optional dev tunnel (exposes the API publicly for webhook/mobile testing):
```bash
dotnet run --project src/StarterApp.AppHost -- --devtunnel
```

### 2. Access the Dashboard

The Aspire dashboard opens automatically. The URL is printed in the console on startup — Aspire assigns a dynamic port.

### 3. Explore Your Services

The dashboard shows:
- 📊 **Service Overview**: All running services and their status
- 🔗 **Endpoints**: Direct links to each service
- 📈 **Metrics**: Real-time performance data
- 📝 **Logs**: Centralized log aggregation
- 🔍 **Traces**: Distributed request tracing

## 🛠️ Service Configuration

### AppHost (`src/StarterApp.AppHost/Program.cs`)

The real topology — simplified for readability:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var seq = builder.AddSeq("seq").WithLifetime(ContainerLifetime.Persistent);
var sql = builder.AddSqlServer("sql").WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("database");
var redis = builder.AddRedis("redis").WithLifetime(ContainerLifetime.Persistent);

// Service Bus topology defined fluently so Aspire serializes correlation filters correctly
var serviceBus = builder.AddAzureServiceBus("servicebus");
var domainEventsTopic = serviceBus.AddServiceBusTopic("domain-events");

domainEventsTopic.AddServiceBusSubscription("email-notifications")
    .WithProperties(sub =>
    {
        sub.Rules.Add(/* correlation filter: EventType == "order.created.v1" */);
        sub.Rules.Add(/* correlation filter: EventType == "order.status-changed.v1" */);
    });

domainEventsTopic.AddServiceBusSubscription("inventory-reservation")
    .WithProperties(sub =>
    {
        sub.Rules.Add(/* correlation filter: EventType == "order.created.v1" */);
    });

serviceBus.RunAsEmulator(e => e.WithLifetime(ContainerLifetime.Persistent));

var migrator = builder.AddProject<Projects.StarterApp_DbMigrator>("migrator")
    .WithReference(db).WaitFor(db);

var api = builder.AddProject<Projects.StarterApp_Api>("api")
    .WithReference(db)
    .WithReference(redis)
    .WithReference(serviceBus)
    .WaitFor(db).WaitFor(redis).WaitFor(serviceBus)
    .WaitForCompletion(migrator);           // API waits for migrator to finish

builder.AddProject<Projects.StarterApp_Functions>("functions")
    .WithReference(serviceBus)
    .WaitFor(serviceBus);

builder.Build().Run();
```

### Key Features

- ✅ **Automatic Service Discovery**: Services find each other by name
- ✅ **Dependency Management**: `WaitFor()` / `WaitForCompletion()` enforce startup order
- ✅ **Configuration Injection**: Connection strings wired automatically from references
- ✅ **Lifecycle Management**: Containers started/stopped as needed (or kept persistent)

## 📊 Observability Features

### Dashboard Components

1. **Service Overview** — real-time health, resource usage, endpoint links
2. **Distributed Tracing** — follow an HTTP request through API → DbContext → Service Bus publish → Functions subscriber
3. **Structured Logging** — centralized from all services, correlation IDs, filterable
4. **Metrics** — HTTP request metrics, DB connection metrics, custom business metrics, runtime metrics

### OpenTelemetry in ServiceDefaults

See [src/StarterApp.ServiceDefaults/Extensions.cs](../../src/StarterApp.ServiceDefaults/Extensions.cs). Both metrics and tracing are configured with ASP.NET Core, HttpClient, and Runtime instrumentation.

## 🔄 Development Workflow

### Recommended Daily Workflow

1. **Start**
   ```bash
   dotnet run --project src/StarterApp.AppHost
   ```

2. **Code with Hot Reload** — edits are picked up without rebuilding containers

3. **Monitor and Debug** — use the dashboard + native .NET debugger breakpoints

4. **Test Integration** — all services come up together; DB migrated automatically; service discovery just works

### Switching Between Environments

**Development with Aspire:**
```bash
dotnet run --project src/StarterApp.AppHost
```

**Production Testing with Docker:**
```bash
docker compose up --build
```

> On arm64 Macs, the `functions` service in Docker Compose fails because
> the Azure Functions isolated-worker image is amd64-only. Aspire runs
> Functions natively via the Functions Core Tools and has no such issue —
> another reason to use Aspire for daily development on Apple Silicon.

## 🔍 Advanced Features

### Health Checks
```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");
```

### Custom Metrics
```csharp
var productCreatedCounter = meter.CreateCounter<int>("products.created");
productCreatedCounter.Add(1, new("category", product.Category));
```

### Dev Tunnel
Pass `--devtunnel` or set `ENABLE_DEV_TUNNEL=true` to publish the API via a tunnel. The AppHost only registers the tunnel when explicitly enabled — it stays off for normal local runs.

## 🧪 Testing Integration

Aspire end-to-end tests live in `StarterApp.AppHost.Tests` and use `DistributedApplicationTestingBuilder` to spin up the full distributed app (SQL Server, Service Bus emulator, API, Functions) and validate the end-to-end pipeline. Tag slow tests with `[Trait("Category", "Aspire")]`.

See [.claude/skills/testing-strategy/SKILL.md](../../.claude/skills/testing-strategy/SKILL.md) for details.

## 🔧 Troubleshooting

**Service won't start** — Check the dashboard logs tab for the failing resource; verify all `WaitFor()` dependencies are healthy.

**Database connection issues** — Confirm the SQL Server container is healthy in the dashboard; verify the migrator completed successfully (the API waits for it).

**Service Bus emulator not ready** — The emulator has a longer start-up than the API; `WaitFor(serviceBus)` ensures ordering.

**Missing telemetry** — Make sure the project calls `builder.Services.AddServiceDefaults();` and the OpenTelemetry instrumentation packages are referenced.

## 🎯 Best Practices

- Use Aspire for daily development (hot reload, debugger, dashboard)
- Use Docker Compose for production-parity integration testing
- Deploy production workloads as containers
- Keep `AddServiceDefaults()` on all new projects for consistent observability

## 🔗 Related Documentation

- [Docker Setup (Step 3)](../03-docker-setup/README.md) - Docker Compose configuration
- [API Endpoints](../API-ENDPOINTS.md) - Minimal API endpoint reference
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/) - Official Microsoft docs

---

**💡 Tip**: Start your development session with Aspire for the rich debugging experience, then validate with Docker Compose before committing changes.
