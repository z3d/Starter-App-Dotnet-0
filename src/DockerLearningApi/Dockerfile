# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first for better layer caching
COPY ["DockerLearningApi/DockerLearningApi.csproj", "DockerLearningApi/"]
COPY ["DockerLearning.Domain/DockerLearning.Domain.csproj", "DockerLearning.Domain/"]
COPY ["DockerLearning.DbMigrator/DockerLearning.DbMigrator.csproj", "DockerLearning.DbMigrator/"]

# Restore dependencies
RUN dotnet restore "DockerLearningApi/DockerLearningApi.csproj"

# Copy the rest of the code but exclude the DbMigrator's appsettings.json to avoid conflict
COPY . .
RUN rm -f /src/DockerLearning.DbMigrator/appsettings.json

# Build the project
WORKDIR "/src/DockerLearningApi"
RUN dotnet build "DockerLearningApi.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "DockerLearningApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Install SQL Server tools and curl for healthcheck
RUN apt-get update && apt-get install -y curl gnupg2 && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/debian/11/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && ACCEPT_EULA=Y apt-get install -y msodbcsql18 mssql-tools18 && \
    echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Create the wait-for-db.sh script directly in the container
RUN echo '#!/bin/bash\n\
\n\
set -e\n\
\n\
echo "Waiting for SQL Server to start..."\n\
\n\
# Set connection timeout variables\n\
MAX_RETRIES=60\n\
RETRY_INTERVAL=5\n\
RETRY_COUNT=0\n\
DB_READY=false\n\
\n\
# Extract host and port from connection string if needed\n\
DB_HOST="db"\n\
DB_PORT="1433"\n\
\n\
# Wait for database to be ready\n\
while [ $RETRY_COUNT -lt $MAX_RETRIES ] && [ "$DB_READY" = "false" ]; do\n\
    echo "SQL Server connection attempt ${RETRY_COUNT}/${MAX_RETRIES}..."\n\
    \n\
    if /opt/mssql-tools18/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -C -Q "SELECT 1" &> /dev/null; then\n\
        echo "SQL Server is now accepting connections!"\n\
        DB_READY=true\n\
    else\n\
        echo "SQL Server not ready yet..."\n\
        RETRY_COUNT=$((RETRY_COUNT+1))\n\
        sleep $RETRY_INTERVAL\n\
    fi\n\
done\n\
\n\
if [ "$DB_READY" = "false" ]; then\n\
    echo "ERROR: Timed out waiting for SQL Server to start after ${MAX_RETRIES} attempts."\n\
    echo "The application will continue, but may fail if database is not available."\n\
else\n\
    # Wait a bit more to ensure SQL Server is fully initialized\n\
    echo "Giving SQL Server a moment to fully initialize..."\n\
    sleep 5\n\
    \n\
    # Create the database if it does not exist\n\
    echo "Ensuring ProductsDb database exists..."\n\
    /opt/mssql-tools18/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -C -Q "IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '"'"'ProductsDb'"'"') CREATE DATABASE ProductsDb"\n\
fi\n\
\n\
echo "Database migrations will be applied automatically on application startup via DbUp."\n\
echo "Starting application..."\n\
\n\
exec "$@"' > wait-for-db.sh

# Make our migration script executable
RUN chmod +x ./wait-for-db.sh

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080
ENV PATH="$PATH:/opt/mssql-tools18/bin"

EXPOSE 8080

# Set the entry point to use our wait-for-db script
ENTRYPOINT ["./wait-for-db.sh", "dotnet", "DockerLearningApi.dll"]