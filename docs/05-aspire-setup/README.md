# Step 5: .NET Aspire Setup - Modern Cloud-Native Orchestration

## Overview

.NET Aspire provides a superior local development experience with built-in observability, service discovery, and configuration management. This step shows how Aspire complements your Docker setup for cloud-native development.

## Current Status

✅ **Already Configured!** - .NET Aspire is fully set up with:

- **AppHost Project**: Orchestrates all services
- **Service Defaults**: Shared configuration and observability
- **SQL Server Integration**: Containerized database with automatic setup
- **Observability Dashboard**: Built-in monitoring and diagnostics
- **Health Checks**: Comprehensive service monitoring

## 🏗️ Aspire Architecture

```
DockerLearning.AppHost (Orchestrator)
├── 📊 Aspire Dashboard (Built-in observability)
├── 🌐 DockerLearningApi (Enhanced with telemetry)
├── 🗄️ SQL Server Container (Managed lifecycle)
├── 🔄 DockerLearning.DbMigrator (Automatic migrations)
└── 📈 Service Defaults (Shared configuration)
```

## 🆚 Development Approaches Comparison

| Aspect | Docker Compose | .NET Aspire | Winner |
|--------|----------------|-------------|---------|
| **Configuration** | YAML files | C# with IntelliSense | 🥇 Aspire |
| **Service Discovery** | Manual networking | Automatic resolution | 🥇 Aspire |
| **Observability** | Requires setup | Built-in dashboard | 🥇 Aspire |
| **Debugging** | Attach to containers | Native .NET debugging | 🥇 Aspire |
| **Hot Reload** | Container rebuilds | Instant .NET hot reload | 🥇 Aspire |
| **Production Parity** | High | Medium | 🥇 Docker |
| **CI/CD Integration** | Excellent | Good | 🥇 Docker |
| **Learning Curve** | Moderate | Easy for .NET devs | 🥇 Aspire |

## 🚀 Quick Start with Aspire

### 1. Start the Application

```powershell
# Navigate to the AppHost project
cd c:\dev\scratchpad\dockerlearning\src\DockerLearning.AppHost

# Run Aspire orchestration
dotnet run
```

### 2. Access the Dashboard

The Aspire dashboard automatically opens at:
- **Primary**: http://localhost:15061
- **HTTPS**: https://localhost:17113

### 3. Explore Your Services

The dashboard shows:
- 📊 **Service Overview**: All running services and their status
- 🔗 **Endpoints**: Direct links to each service
- 📈 **Metrics**: Real-time performance data
- 📝 **Logs**: Centralized log aggregation
- 🔍 **Traces**: Distributed request tracing

## 🛠️ Service Configuration

### AppHost Configuration (`Program.cs`)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// SQL Server with persistent storage
var sql = builder.AddSqlServer("sql")
                 .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("database");

// API with automatic configuration
builder.AddProject<Projects.DockerLearningApi>("api")
       .WithReference(db)
       .WaitFor(db);

// Database migrator for schema updates
builder.AddProject<Projects.DockerLearning_DbMigrator>("migrator")
       .WithReference(db)
       .WaitFor(db);

builder.Build().Run();
```

### Key Features:

- ✅ **Automatic Service Discovery**: Services find each other by name
- ✅ **Dependency Management**: `WaitFor()` ensures proper startup order
- ✅ **Configuration Injection**: Connection strings automatically provided
- ✅ **Lifecycle Management**: Containers started/stopped as needed

## 📊 Observability Features

### Built-in Dashboard Components

#### 1. **Service Overview**
- Real-time service health status
- Resource consumption metrics
- Replica counts and scaling status
- Direct links to service endpoints

#### 2. **Distributed Tracing**
```
HTTP Request → API Endpoint → Repository → Database
     ↓              ↓             ↓          ↓
  Trace Span    Trace Span   Trace Span  SQL Span
```

#### 3. **Structured Logging**
- Centralized logs from all services
- Correlation IDs for request tracking
- Filterable by service, level, and content
- Integration with existing Serilog configuration

#### 4. **Metrics Collection**
- HTTP request metrics (latency, status codes)
- Database connection metrics
- Custom business metrics
- Memory and CPU usage

### Custom Observability

Your application automatically gets:

```csharp
// In ServiceDefaults/Extensions.cs
services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation();
    })
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddEntityFrameworkCoreInstrumentation();
    });
```

## 🔄 Development Workflow

### Recommended Daily Workflow

1. **Start Development Session**
   ```powershell
   cd src\DockerLearning.AppHost
   dotnet run
   ```

2. **Code with Hot Reload**
   - Make changes to API code
   - See changes reflected immediately
   - No container rebuilds needed

3. **Monitor and Debug**
   - Use Aspire dashboard for observability
   - Set breakpoints in Visual Studio/VS Code
   - View real-time metrics and logs

4. **Test Integration**
   - All services run together
   - Database automatically migrated
   - Service discovery works seamlessly

### Switching Between Environments

**Development with Aspire:**
```powershell
# Rich observability and debugging
cd src\DockerLearning.AppHost
dotnet run
```

**Production Testing with Docker:**
```powershell
# Production-like environment
docker-compose up --build
```

## 🔧 Configuration Management

### Environment-Specific Settings

Aspire automatically handles:

```csharp
// Development configuration
"ConnectionStrings": {
  "DefaultConnection": "Server=127.0.0.1,61430;User ID=sa;Password=Your_password123;TrustServerCertificate=true;Initial Catalog=DockerLearning"
}

// Automatic service discovery
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)); // Injected automatically
```

### Service Defaults Integration

```csharp
// Shared across all services
builder.Services.AddServiceDefaults();

// Provides:
// - Health checks
// - OpenTelemetry
// - Service discovery
// - Configuration management
```

## 🔍 Advanced Features

### Health Checks Integration

```csharp
// Automatic health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");
```

Access health checks at:
- http://localhost:5164/health
- Dashboard shows aggregated health status

### Custom Metrics

```csharp
// Add custom business metrics
var productCreatedCounter = meter.CreateCounter<int>("products.created");

// In your endpoint
productCreatedCounter.Add(1, new("category", product.Category));
```

### Resource Management

```csharp
// Configure resource limits
builder.AddProject<Projects.DockerLearningApi>("api")
       .WithReplicas(2)                    // Multiple instances
       .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
```

## 🧪 Testing Integration

### Integration Tests with Aspire

```csharp
public class AspireIntegrationTests : IClassFixture<DistributedApplicationTestingBuilder>
{
    [Fact]
    public async Task GetProducts_ReturnsSuccessfully()
    {
        var app = await builder.BuildAsync();
        var httpClient = app.CreateHttpClient("api");
        
        var response = await httpClient.GetAsync("/api/products");
        
        response.EnsureSuccessStatusCode();
    }
}
```

## 🔧 Troubleshooting

### Common Issues

**Port Conflicts:**
```powershell
# Aspire uses dynamic ports - check dashboard for actual URLs
# If port 15061 is busy, Aspire will choose another port
```

**Service Won't Start:**
```powershell
# Check logs in the dashboard
# Verify all dependencies are resolved
# Ensure Docker is running for SQL Server container
```

**Database Connection Issues:**
```powershell
# Verify SQL Server container is running
docker ps | findstr sql

# Check connection string in dashboard
# Ensure migrations have run successfully
```

**Missing Telemetry:**
```powershell
# Ensure ServiceDefaults is added
builder.Services.AddServiceDefaults();

# Check OpenTelemetry configuration
# Verify instrumentation packages are installed
```

### Debugging Tips

1. **Use the Dashboard**: Primary debugging tool
2. **Check Dependencies**: Ensure `WaitFor()` is configured
3. **View Logs**: Centralized in the dashboard
4. **Monitor Metrics**: Real-time performance data
5. **Trace Requests**: Follow request flow across services

## 🎯 Best Practices

### Development
- ✅ Use Aspire for daily development
- ✅ Leverage hot reload for faster iteration
- ✅ Monitor metrics during development
- ✅ Use structured logging consistently

### Testing
- ✅ Test with Docker Compose for production parity
- ✅ Use Aspire testing framework for integration tests
- ✅ Validate service discovery and configuration

### Production
- ✅ Deploy with Docker containers
- ✅ Use learned observability patterns
- ✅ Apply similar health check strategies
- ✅ Implement same metrics in production

## 🌟 Summary

.NET Aspire provides:

- **🚀 Enhanced Developer Experience**: Hot reload, native debugging, IntelliSense
- **📊 Built-in Observability**: Dashboard, metrics, logging, tracing
- **🔧 Simplified Configuration**: Service discovery, automatic connection strings
- **🔍 Better Debugging**: Rich diagnostics and monitoring tools
- **⚡ Faster Iteration**: No container rebuilds for code changes

### When to Use What

- **Daily Development**: .NET Aspire for rich experience
- **Integration Testing**: Docker Compose for production parity  
- **CI/CD Pipelines**: Docker for consistent environments
- **Production Deployment**: Containers with learned patterns

## 🎉 Congratulations!

You've completed the full tutorial! You now have:

- ✅ A modern .NET 8 Web API with clean architecture
- ✅ SQL Server with automated migrations
- ✅ Docker containerization for production
- ✅ Azure deployment capabilities
- ✅ .NET Aspire for enhanced development

### Next Steps

- Explore additional Aspire integrations (Redis, RabbitMQ, etc.)
- Implement custom metrics and monitoring
- Set up automated CI/CD pipelines
- Add more sophisticated health checks
- Experiment with scaling and load testing

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
