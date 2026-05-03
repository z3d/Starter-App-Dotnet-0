# 🚀 .NET Aspire Setup Complete!

This project has **side-by-side** .NET Aspire orchestration alongside the Docker Compose setup.

## ✅ **What's Included**

### Projects
- **`StarterApp.AppHost`** - Aspire orchestration host
- **`StarterApp.ServiceDefaults`** - Shared Aspire configuration

### Enhanced API
- Aspire ServiceDefaults for observability
- Built-in health checks and telemetry
- Service discovery integration

## 🎯 **Quick Start**

### Option 1: Run with Aspire (Rich Debugging)
```bash
dotnet run --project src/StarterApp.AppHost
```
- **Aspire Dashboard**: Opens automatically in browser
- **Rich Observability**: Distributed tracing, metrics, logs
- **Service Health**: Real-time status monitoring

### Option 2: Run with Docker Compose (Production-like)
```bash
docker compose up --build
```
- **API**: http://localhost:8080
- **Scalar API Reference**: http://localhost:8080/scalar/v1

> **Note (Apple Silicon)**: The Azure Functions base image
> `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0`
> is currently published for `linux/amd64` only. On arm64 Macs, run via
> Aspire (which uses the locally-installed Functions Core Tools natively)
> or start Docker Compose without the `functions` service.

## 🔍 **Aspire Features**

1. **Automatic Dashboard**: Opens at the URL printed by AppHost on startup
2. **Service Map**: Visual representation of your services
3. **Distributed Tracing**: Follow requests across API → Database → Service Bus
4. **Real-time Metrics**: Performance counters and custom metrics
5. **Centralized Logs**: All service logs in one place
6. **Configuration View**: Live view of all service settings

## 📊 **What the Dashboard Shows**

- **Services**: API, SQL Server, Redis, Blob storage emulator, Service Bus emulator, Functions, DbMigrator, Seq
- **Traces**: HTTP requests, database queries, Service Bus publishes, custom operations
- **Metrics**: Request duration, error rates, throughput
- **Logs**: Structured logging with correlation IDs
- **Resources**: Container health and resource usage

## 🎛️ **Development Workflow**

### Recommended Approach
```bash
# Daily development - rich debugging
dotnet run --project src/StarterApp.AppHost

# Integration testing - production-like
docker compose up

# Switch between approaches as needed
```

## 🔧 **Troubleshooting**

### If Aspire doesn't start:
1. Check that .NET 10 SDK is installed: `dotnet --version`
2. Ensure Aspire workload is installed: `dotnet workload list`
3. Check the console output for specific error messages

### If services don't connect:
- The Aspire dashboard shows real-time service health
- Check the "Resources" tab for container status
- Review logs in the "Logs" tab for connection issues

## 📋 **Next Steps**

1. **Try the Aspire Dashboard**: Start with `dotnet run --project src/StarterApp.AppHost`
2. **Compare Experiences**: Use both Aspire and Docker Compose
3. **Explore Observability**: Add custom metrics and traces
4. **Run Tests**: `dotnet test` works with both setups

---

## 📚 **Documentation**

- **Aspire Setup**: [docs/05-aspire-setup/README.md](docs/05-aspire-setup/README.md)
- **Docker Setup**: [docs/03-docker-setup/README.md](docs/03-docker-setup/README.md)
- **API Endpoints**: [docs/API-ENDPOINTS.md](docs/API-ENDPOINTS.md)
