# Step 3: Docker Setup

## Overview
In this step, we'll containerize both our .NET Web API and SQL Server using Docker, making our application portable and easier to deploy.

## Instructions

### 1. Create a Dockerfile for our .NET Web API:

Create a file named `Dockerfile` in the API project directory:

```dockerfile
# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["DockerLearningApi.csproj", "./"]
RUN dotnet restore

# Copy the rest of the code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

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
      context: ./src/DockerLearningApi
      dockerfile: Dockerfile
    ports:
      - "8080:8080"
      - "8081:8081"
    environment:
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Server=db;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
    depends_on:
      - db
    networks:
      - backend-network

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

networks:
  backend-network:
    driver: bridge

volumes:
  sqldata:
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

### 4. Create a script for database initialization in Docker environment:

Create a folder in your API project named `Scripts` and add a file named `wait-for-db.sh`:

```bash
#!/bin/bash

set -e

until dotnet ef database update; do
  >&2 echo "SQL Server is starting up, waiting..."
  sleep 1
done

>&2 echo "SQL Server is up - executing command"
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
- Swagger UI at http://localhost:8080/swagger

### 7. Stopping the containers:

```bash
# Stop and remove the containers
docker-compose down

# To also remove volumes (will delete database data)
docker-compose down -v
```

### Next Step
Proceed to [Step 4: Azure Deployment](../04-azure-deployment/README.md) to deploy your containerized application to Azure.