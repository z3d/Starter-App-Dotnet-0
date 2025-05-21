# Step 3: Docker Setup

## Overview
In this step, we'll containerize both our .NET Web API and SQL Server using Docker, making our application portable and easier to deploy.

## Instructions

### 1. Create a Dockerfile for our .NET Web API:

Create a file named `Dockerfile` in the API project directory:

```dockerfile
# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first for better layer caching
COPY ["DockerLearningApi/DockerLearningApi.csproj", "DockerLearningApi/"]
COPY ["DockerLearning.Domain/DockerLearning.Domain.csproj", "DockerLearning.Domain/"]
COPY ["DockerLearning.DbMigrator/DockerLearning.DbMigrator.csproj", "DockerLearning.DbMigrator/"]

# Restore dependencies
RUN dotnet restore "DockerLearningApi/DockerLearningApi.csproj"

# Copy the rest of the code and build
COPY . .
RUN dotnet build "DockerLearningApi/DockerLearningApi.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "DockerLearningApi/DockerLearningApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Install SQL Server tools for database connection check
RUN apt-get update && apt-get install -y curl gnupg2 && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/debian/11/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && ACCEPT_EULA=Y apt-get install -y msodbcsql18 mssql-tools18 && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

# Set the entry point
ENTRYPOINT ["dotnet", "DockerLearningApi.dll"]
```

### 2. Create a docker-compose.yml file for the entire solution:

Create a file named `docker-compose.yml` in the root directory:

```yaml
version: '3.8'

services:
  api:
    build:
      context: ./src
      dockerfile: DockerLearningApi/Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Docker
      - ConnectionStrings__DefaultConnection=Server=db;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 10s
    depends_on:
      - db
    networks:
      - backend-network
    restart: unless-stopped

  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Passw0rd
      - MSSQL_PID=Developer
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql
    networks:
      - backend-network
    restart: unless-stopped

networks:
  backend-network:
    driver: bridge

volumes:
  sqldata:
    driver: local
```

### 3. Update the `appsettings.json` for Docker:

Create a new configuration file named `appsettings.Docker.json` in your API project:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### 4. Create a robust script for database initialization in Docker environment:

Create a folder in your API project named `Scripts` and add a file named `wait-for-db.sh`:

```bash
#!/bin/bash

set -e

echo "Waiting for SQL Server to start..."

# Set connection timeout variables
MAX_RETRIES=60
RETRY_INTERVAL=5
RETRY_COUNT=0
DB_READY=false

# Extract host and port from connection string if needed
DB_HOST="db"
DB_PORT="1433"

# Wait for database to be ready
while [ $RETRY_COUNT -lt $MAX_RETRIES ] && [ "$DB_READY" = "false" ]; do
    echo "SQL Server connection attempt ${RETRY_COUNT}/${MAX_RETRIES}..."
    
    if /opt/mssql-tools18/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -Q "SELECT 1" &> /dev/null; then
        echo "SQL Server is now accepting connections!"
        DB_READY=true
    else
        echo "SQL Server not ready yet..."
        RETRY_COUNT=$((RETRY_COUNT+1))
        sleep $RETRY_INTERVAL
    fi
done

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