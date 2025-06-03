# Step 5: .NET Aspire Setup (Side-by-Side with Docker)

## Overview

This step adds .NET Aspire as an alternative to Docker Compose for local development orchestration. Aspire provides built-in observability, service discovery, and configuration management while maintaining compatibility with your existing Docker setup.

## 🏗️ **Aspire Architecture**

```
DockerLearning.AppHost          ← Orchestrates all services
├── DockerLearningApi           ← Your existing API (enhanced with Aspire)
├── DockerLearning.DbMigrator   ← Database migrations
├── SQL Server (Container)      ← Managed by Aspire
└── Aspire Dashboard           ← Built-in observability
```

## 🆚 **Docker Compose vs Aspire Comparison**

| Feature | Docker Compose | .NET Aspire |
|---------|----------------|-------------|
| **Orchestration** | YAML configuration | C# code with IntelliSense |
| **Service Discovery** | Manual DNS/networking | Automatic service resolution |
| **Observability** | Manual setup required | Built-in dashboard with traces |
| **Configuration** | Environment variables | Structured .NET configuration |
| **Health Checks** | Basic container health | Rich .NET health endpoints |
| **Development Experience** | Docker logs | Rich dashboard with distributed tracing |
| **Hot Reload** | Container rebuild | .NET hot reload support |
| **Debugging** | Attach to container | Native .NET debugging |

## 🚀 **Running with Aspire**

### Start Aspire Orchestration
```powershell
# Navigate to the AppHost project
cd c:\dev\scratchpad\dockerlearning\src\DockerLearning.AppHost

# Run the Aspire orchestration
dotnet run
```

### Access Services
- **Aspire Dashboard**: http://localhost:15255 (automatically opens)
- **API**: http://localhost:5000 (or as shown in dashboard)
- **API Documentation**: http://localhost:5000/openapi/v1.json

### Stop Services
- Press `Ctrl+C` in the terminal running the AppHost
- All services will gracefully shut down

## 🚀 **Running with Docker Compose (Existing)**

Your existing Docker setup continues to work unchanged:

```powershell
# From solution root
cd c:\dev\scratchpad\dockerlearning

# Start with Docker Compose
docker-compose up --build

# Access services
# API: http://localhost:8080
# Swagger: http://localhost:8080/swagger
```

## 📊 **Aspire Dashboard Features**

The Aspire dashboard provides:

1. **Service Overview**: All services and their health status
2. **Distributed Tracing**: Request flows across services
3. **Metrics**: Performance counters and custom metrics
4. **Logs**: Centralized logging from all services
5. **Configuration**: Live view of service configuration
6. **Environment Variables**: Real-time configuration values

## 🔧 **Configuration**

### Aspire Configuration (`AppHost/Program.cs`)
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// SQL Server with persistent volume
var sqlServer = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .AddDatabase("ProductsDb");

// API with service discovery and health checks
var api = builder.AddProject<Projects.DockerLearningApi>("api")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

// Database migrator runs on startup
var migrator = builder.AddProject<Projects.DockerLearning_DbMigrator>("migrator")
    .WithReference(sqlServer)
    .WaitFor(sqlServer);

builder.Build().Run();
```

### Service Defaults Integration
Your API automatically gets:
- **OpenTelemetry**: Distributed tracing and metrics
- **Health Checks**: Built-in health endpoints
- **Service Discovery**: Automatic connection string resolution
- **Configuration**: Environment-specific settings

## 🔍 **Observability Features**

### Distributed Tracing
- Automatic request correlation across services
- Database query tracing
- HTTP request/response tracking
- Custom trace spans for business operations

### Metrics
- HTTP request metrics (duration, status codes)
- Database connection metrics
- Custom business metrics
- Performance counters

### Structured Logging
- Centralized log aggregation
- Correlation IDs for request tracking
- Structured log events with contexts
- Integration with your existing Serilog setup

## 🛠️ **Development Workflow**

### Recommended Approach
1. **Daily Development**: Use Aspire for rich debugging and observability
2. **Integration Testing**: Use Docker Compose for production-like environment
3. **CI/CD**: Use Docker Compose for consistent build environments

### Switching Between Approaches

**Start with Aspire:**
```powershell
cd src\DockerLearning.AppHost
dotnet run
```

**Switch to Docker:**
```powershell
# Stop Aspire (Ctrl+C)
# Start Docker
docker-compose up
```

## 🔧 **Troubleshooting**

### Common Issues

**Issue**: Services not starting
**Solution**: Check the Aspire dashboard for detailed error messages

**Issue**: Database connection failures
**Solution**: Verify SQL Server container is healthy in the dashboard

**Issue**: Port conflicts
**Solution**: Aspire will automatically assign available ports

### Debugging
1. **Use the Dashboard**: Real-time service status and logs
2. **Visual Studio Integration**: Set multiple startup projects
3. **Distributed Tracing**: Follow requests across service boundaries

## 🚀 **Advanced Scenarios**

### Adding New Services
```csharp
// In AppHost/Program.cs
var redis = builder.AddRedis("redis");
var api = builder.AddProject<Projects.DockerLearningApi>("api")
    .WithReference(sqlServer)
    .WithReference(redis);  // Add Redis reference
```

### Custom Health Checks
```csharp
// In API Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<ExternalServiceHealthCheck>("external-api");
```

### Environment Configuration
```csharp
// Different configurations per environment
if (builder.Environment.IsDevelopment())
{
    // Development-specific services
    var jaeger = builder.AddContainer("jaeger", "jaegertracing/all-in-one")
        .WithBindMount("./jaeger", "/tmp");
}
```

## 📋 **Next Steps**

1. **Try Both Approaches**: Compare development experience
2. **Explore the Dashboard**: Discover observability features
3. **Add Custom Metrics**: Enhance monitoring
4. **Integration Testing**: Combine with your convention tests

## 🔗 **Related Documentation**

- [Docker Setup (Step 3)](../03-docker-setup/README.md) - Original Docker configuration
- [Convention Tests](../../src/DockerLearningApi.Tests/Conventions/README.md) - Architectural testing
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/) - Official Microsoft docs

---

**💡 Tip**: Start your development session with Aspire to leverage the rich debugging experience, then validate with Docker Compose before committing changes.
