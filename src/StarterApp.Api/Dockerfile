# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
WORKDIR /src

# Copy Directory.Build.props + Directory.Packages.props for shared configuration
# (CPM requires Directory.Packages.props at restore time)
COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY [".editorconfig", ".editorconfig"]
COPY ["BannedSymbols.txt", "BannedSymbols.txt"]
COPY ["NuGet.config", "NuGet.config"]

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

# Publish the application. Style is enforced by the format gate and the CI build on the
# canonical checkout; the image build only packages. Re-running IDE style analysis here makes
# the build depend on the checkout's line endings (a CRLF working tree fails IDE0055), so a
# Windows clone could not build the image. Same workaround as the Functions Dockerfile.
WORKDIR "/src/src/StarterApp.Api"
RUN dotnet publish "StarterApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false -p:EnforceCodeStyleInBuild=false

# Runtime image: chiseled (no shell, no apt, globalization-invariant), nothing but the runtime.
# The API uses the Npgsql managed driver; database inspection should happen from the PostgreSQL container or psql-capable tooling.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:48e51f2f6798897be7ac4e775c049ed8fe60d3190f637e1f9c9dc7513efa659c AS final

WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

# The chiseled base already runs as UID 1654 ($APP_UID); kept explicit for the
# ShippingDockerfiles_MustRunTheFinalStageAsNonRoot convention test.
USER $APP_UID

# Start the application — migrations handled by the dedicated migrator service
ENTRYPOINT ["dotnet", "StarterApp.Api.dll"]
