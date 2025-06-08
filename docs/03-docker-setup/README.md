# Step 3: Docker Setup and Containerization

## Overview

This step covers containerizing the .NET Web API and SQL Server using Docker. The setup provides a production-like environment that can be easily deployed and shared across teams.

## Current Status

✅ **Already Configured!** - Docker setup is complete with:

- **Multi-stage Dockerfile** for the .NET API
- **Docker Compose** orchestration for API + SQL Server
- **Health checks** for service monitoring
- **Volume persistence** for database data
- **Environment-specific configuration**

## Docker Architecture

```
Docker Environment
├── api container (DockerLearningApi)
│   ├── .NET 8 Runtime
│   ├── Published application
│   └── Health check endpoint
├── db container (SQL Server 2022)
│   ├── SQL Server Database Engine
│   ├── Persistent data volume
│   └── Database initialization
└── backend-network (bridge network)
```

## Files Overview

### 1. Dockerfile (`src/DockerLearningApi/Dockerfile`)

**Multi-stage build process:**
```dockerfile
# Build stage - uses SDK image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Runtime stage - uses optimized runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
```

**Key features:**
- ✅ Multi-stage build for smaller final image
- ✅ Layer caching optimization
- ✅ Security best practices
- ✅ Health monitoring tools

### 2. Docker Compose (`docker-compose.yml`)

**Services:**
- **api**: .NET Web API container
- **db**: SQL Server 2022 container

**Features:**
- ✅ Service dependency management
- ✅ Health checks for both services
- ✅ Persistent volume for database
- ✅ Custom network isolation
- ✅ Environment variable configuration

## Running with Docker

### Quick Start
```powershell
# From the solution root directory
cd c:\dev\scratchpad\dockerlearning

# Build and start all services
docker-compose up --build
```

### Step-by-Step Process

1. **Build the images:**
   ```powershell
   docker-compose build
   ```

2. **Start the services:**
   ```powershell
   docker-compose up -d
   ```

3. **View logs:**
   ```powershell
   # All services
   docker-compose logs -f
   
   # Specific service
   docker-compose logs -f api
   docker-compose logs -f db
   ```

4. **Check service health:**
   ```powershell
   docker-compose ps
   ```

## Service Endpoints

Once running, access the services at:

| Service | URL | Description |
|---------|-----|-------------|
| **API** | http://localhost:8080 | Main API endpoint |
| **Swagger** | http://localhost:8080/swagger | API documentation |
| **Health Check** | http://localhost:8080/health | Service health status |
| **SQL Server** | localhost:1433 | Database connection |

## Configuration Details

### Environment Variables

**API Container:**
```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Docker
  - ConnectionStrings__DefaultConnection=Server=db;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
  - ASPNETCORE_HTTP_PORTS=8080
```

**Database Container:**
```yaml
environment:
  - ACCEPT_EULA=Y
  - SA_PASSWORD=YourStrong@Passw0rd
  - MSSQL_PID=Developer
```

### Health Checks

**API Health Check:**
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 10s
```

**SQL Server Health Check:**
```yaml
healthcheck:
  test: ["CMD-SHELL", "/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourStrong@Passw0rd -Q 'SELECT 1'"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 30s
```

## Data Persistence

### Volume Configuration
```yaml
volumes:
  sqldata:
    driver: local
```

**Benefits:**
- ✅ Database data survives container restarts
- ✅ Data persists across Docker Compose down/up cycles
- ✅ Can be backed up independently

### Volume Management
```powershell
# List volumes
docker volume ls

# Inspect volume
docker volume inspect dockerlearning_sqldata

# Backup volume (example)
docker run --rm -v dockerlearning_sqldata:/data -v ${PWD}:/backup ubuntu tar czf /backup/sqldata-backup.tar.gz -C /data .
```

## Networking

### Custom Network
```yaml
networks:
  backend-network:
    driver: bridge
```

**Features:**
- ✅ Service discovery by name (api can reach db by hostname)
- ✅ Network isolation from other Docker projects
- ✅ Custom DNS resolution within the network

## Database Initialization

### Automatic Schema Creation

The API automatically runs database migrations on startup through the configured startup process. This ensures:

- ✅ Database is created if it doesn't exist
- ✅ Schema is up-to-date with latest migrations
- ✅ Sample data is seeded for testing

### Manual Database Operations

```powershell
# Connect to SQL Server container
docker exec -it dockerlearning-db-1 /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourStrong@Passw0rd

# Run migrations manually
docker-compose exec api dotnet DockerLearning.DbMigrator.dll
```

## Docker Commands Reference

### Basic Operations
```powershell
# Build and start
docker-compose up --build

# Start detached
docker-compose up -d

# Stop services
docker-compose down

# Stop and remove volumes
docker-compose down -v

# View logs
docker-compose logs -f

# Scale services (if needed)
docker-compose up --scale api=2
```

### Development Commands
```powershell
# Rebuild specific service
docker-compose build api

# Restart specific service
docker-compose restart api

# Execute command in running container
docker-compose exec api bash

# View container processes
docker-compose top
```

### Cleanup Commands
```powershell
# Remove all containers and networks
docker-compose down

# Remove including volumes (⚠️ loses data)
docker-compose down -v

# Remove unused Docker resources
docker system prune

# Remove all stopped containers
docker container prune
```

## Troubleshooting

### Common Issues

**Port Already in Use**
```powershell
# Check what's using the port
netstat -ano | findstr :8080
netstat -ano | findstr :1433

# Stop process or change ports in docker-compose.yml
```

**Container Won't Start**
```powershell
# Check logs for errors
docker-compose logs api
docker-compose logs db

# Check container status
docker-compose ps
```

**Database Connection Issues**
```powershell
# Verify SQL Server is ready
docker-compose exec db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P YourStrong@Passw0rd -Q "SELECT @@VERSION"

# Check network connectivity
docker-compose exec api ping db
```

**Build Failures**
```powershell
# Clean and rebuild
docker-compose down
docker-compose build --no-cache
docker-compose up
```

### Health Check Monitoring

```powershell
# Check health status
docker-compose ps

# View health check logs
docker inspect dockerlearning-api-1 | grep -A 10 Health
docker inspect dockerlearning-db-1 | grep -A 10 Health
```

## Production Considerations

### Security
- ✅ Use secrets management for passwords
- ✅ Run containers as non-root users
- ✅ Scan images for vulnerabilities
- ✅ Use specific image tags, not 'latest'

### Performance
- ✅ Configure appropriate resource limits
- ✅ Use multi-stage builds to minimize image size
- ✅ Optimize layer caching
- ✅ Consider using Alpine-based images for smaller size

### Monitoring
- ✅ Health checks are configured
- ✅ Consider adding application metrics
- ✅ Set up log aggregation
- ✅ Monitor resource usage

## Next Step

Continue to **[Step 4: Azure Deployment](../04-azure-deployment/README.md)** to deploy the containerized application to Azure Container Apps.

if [ "$DB_READY" = "false" ]; then
    echo "ERROR: Timed out waiting for SQL Server to start after ${MAX_RETRIES} attempts."
    echo "The application will continue, but may fail if database is not available."
else
    # Wait a bit more to ensure SQL Server is fully initialized
    echo "Giving SQL Server a moment to fully initialize..."
    sleep 5
    
    # Create the database if it does not exist
    echo "Ensuring ProductsDb database exists..."
    /opt/mssql-tools18/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -Q "IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ProductsDb') CREATE DATABASE ProductsDb"
fi

echo "Database migrations will be applied automatically on application startup via DbUp."
echo "Starting application..."

exec "$@"
```

Make sure to make the script executable:

```bash
chmod +x ./Scripts/wait-for-db.sh
```

Then update your Dockerfile to use the wait-for-db.sh script:

```dockerfile
# ... existing code ...

# Copy wait-for-db.sh script and make it executable
COPY --from=build /src/DockerLearningApi/Scripts/wait-for-db.sh ./
RUN chmod +x ./wait-for-db.sh

# ... existing code ...

# Set the entry point to use our wait-for-db script
ENTRYPOINT ["./wait-for-db.sh", "dotnet", "DockerLearningApi.dll"]
```

### 5. Running the application with Docker Compose:

```bash
# Navigate to the root directory of the project
cd c:\dev\scratchpad\dockerlearning

# Build and start the containers
docker-compose up --build

# To run in detached mode (background)
docker-compose up -d --build
```

### 6. Access the API:

Once the containers are running, you can access:
- The API at http://localhost:8080
- API documentation at http://localhost:8080/swagger

### 7. Stopping the containers:

```bash
# Stop and remove the containers
docker-compose down

# To also remove volumes (will delete database data)
docker-compose down -v
```

### Troubleshooting

If you encounter an error like `exec ./wait-for-db.sh: no such file or directory`, ensure that:
1. The wait-for-db.sh script is properly copied to the container
2. The script has the correct Unix line endings (LF, not CRLF)
3. The script has execute permissions

You can also create the script directly in the Dockerfile as follows:

```dockerfile
# Create the wait-for-db.sh script directly in the container
RUN echo '#!/bin/bash\n\
echo "Waiting for SQL Server to start..."\n\
# ... rest of the script content
' > wait-for-db.sh

RUN chmod +x ./wait-for-db.sh
```

### Next Step
Proceed to [Step 4: Azure Deployment](../04-azure-deployment/README.md) to deploy your containerized application to Azure.