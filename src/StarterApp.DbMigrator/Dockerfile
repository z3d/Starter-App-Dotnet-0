# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:548d93f8a18a1acbe6cc127bc4f47281430d34a9e35c18afa80a8d6741c2adc3 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY [".editorconfig", ".editorconfig"]
COPY ["NuGet.config", "NuGet.config"]
COPY ["src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.DbMigrator/packages.lock.json", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]
COPY ["src/StarterApp.ServiceDefaults/packages.lock.json", "src/StarterApp.ServiceDefaults/"]

# --locked-mode: fail the build if the resolved graph diverges from packages.lock.json,
# matching CI, so the image ships exactly the audited dependency closure. Required here
# because the RestoreLockedMode MSBuild prop is gated on $(CI), unset inside docker build.
RUN dotnet restore "src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj" --locked-mode

COPY src/ src/

WORKDIR "/src/src/StarterApp.DbMigrator"
RUN dotnet publish "StarterApp.DbMigrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage — lightweight, no ODBC tools needed
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Drop root: run the migration job as the aspnet base image's predefined non-root user
# (UID 1654, via $APP_UID). DbUp only reads embedded scripts and connects to PostgreSQL,
# so it needs no write access or elevated privileges.
USER $APP_UID

ENTRYPOINT ["dotnet", "StarterApp.DbMigrator.dll"]
