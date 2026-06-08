# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Directory.Build.props + Directory.Packages.props for shared configuration
# (CPM requires Directory.Packages.props at restore time)
COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY [".editorconfig", ".editorconfig"]

# Copy csproj + packages.lock.json files first for better layer caching
COPY ["src/StarterApp.Api/StarterApp.Api.csproj", "src/StarterApp.Api/"]
COPY ["src/StarterApp.Api/packages.lock.json", "src/StarterApp.Api/"]
COPY ["src/StarterApp.Domain/StarterApp.Domain.csproj", "src/StarterApp.Domain/"]
COPY ["src/StarterApp.Domain/packages.lock.json", "src/StarterApp.Domain/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]
COPY ["src/StarterApp.ServiceDefaults/packages.lock.json", "src/StarterApp.ServiceDefaults/"]

# Restore dependencies. --locked-mode makes the image build fail if the resolved graph
# diverges from packages.lock.json (the lock files are COPY'd in above), matching what CI
# enforces — so the deployed artifact contains exactly the audited dependency closure and
# cannot silently pull a different transitive version. (The MSBuild RestoreLockedMode prop
# is gated on $(CI), which is not set inside docker build, so the flag is required here.)
RUN dotnet restore "src/StarterApp.Api/StarterApp.Api.csproj" --locked-mode

# Copy the rest of the source code
COPY src/ src/

# Build the project
WORKDIR "/src/src/StarterApp.Api"
RUN dotnet build "StarterApp.Api.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "StarterApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image — lightweight, no ODBC tools needed
# The API uses the Npgsql managed driver; database inspection should happen from the PostgreSQL container or psql-capable tooling.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

# Drop root: the aspnet base image ships a predefined non-root user (UID 1654) exposed
# via $APP_UID. Listening on 8080 (>1024) needs no privileged bind, and the published
# files are world-readable, so the app runs unchanged with least privilege. Hardened
# orchestrators (Kubernetes runAsNonRoot, OpenShift, CIS benchmarks) require this.
USER $APP_UID

# Start the application — migrations handled by the dedicated migrator service
ENTRYPOINT ["dotnet", "StarterApp.Api.dll"]
