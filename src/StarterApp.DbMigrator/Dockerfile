# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY [".editorconfig", ".editorconfig"]
COPY ["BannedSymbols.txt", "BannedSymbols.txt"]
COPY ["NuGet.config", "NuGet.config"]
COPY ["src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.DbMigrator/packages.lock.json", "src/StarterApp.DbMigrator/"]

# --locked-mode: fail the build if the resolved graph diverges from packages.lock.json,
# matching CI, so the image ships exactly the audited dependency closure. Required here
# because the RestoreLockedMode MSBuild prop is gated on $(CI), unset inside docker build.
RUN dotnet restore "src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj" --locked-mode

COPY src/ src/

# Style analysis is skipped here for the same reason as the Api and Functions Dockerfiles: a
# CRLF working tree fails IDE0055 inside the image build, and the CI build already enforces it.
WORKDIR "/src/src/StarterApp.DbMigrator"
RUN dotnet publish "StarterApp.DbMigrator.csproj" -c Release -o /app/publish /p:UseAppHost=false -p:EnforceCodeStyleInBuild=false

# Runtime stage: console app, so the plain runtime (not aspnet), chiseled (no shell, no apt).
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:7ac579eeb88591c261c85576d7094e8f65949ff9e9502cdaab1de2457e506505 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The chiseled base already runs as UID 1654 ($APP_UID); kept explicit for the
# ShippingDockerfiles_MustRunTheFinalStageAsNonRoot convention test.
USER $APP_UID

ENTRYPOINT ["dotnet", "StarterApp.DbMigrator.dll"]
