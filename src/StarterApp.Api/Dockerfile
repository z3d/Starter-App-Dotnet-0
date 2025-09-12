# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy Directory.Build.props for shared configuration
COPY ["Directory.Build.props", "Directory.Build.props"]

# Copy csproj files first for better layer caching  
COPY ["src/StarterApp.Api/StarterApp.Api.csproj", "src/StarterApp.Api/"]
COPY ["src/StarterApp.Domain/StarterApp.Domain.csproj", "src/StarterApp.Domain/"]
COPY ["src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/StarterApp.Api/StarterApp.Api.csproj"

# Copy the rest of the code but exclude the DbMigrator's appsettings.json to avoid conflict
COPY . .
RUN rm -f /src/src/StarterApp.DbMigrator/appsettings.json

# Build the project
WORKDIR "/src/src/StarterApp.Api"
RUN dotnet build "StarterApp.Api.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "StarterApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Install SQL Server tools for debugging if needed (optional)
RUN apt-get update && apt-get install -y curl gnupg2 && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/debian/11/prod.list > /etc/apt/sources.list.d/mssql-release.list && \
    apt-get update && ACCEPT_EULA=Y apt-get install -y msodbcsql18 mssql-tools18 && \
    echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080
ENV PATH="$PATH:/opt/mssql-tools18/bin"

EXPOSE 8080

# Start the application directly - DbUp handles database connectivity
ENTRYPOINT ["dotnet", "StarterApp.Api.dll"]