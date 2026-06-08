# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["Directory.Packages.props", "Directory.Packages.props"]
COPY [".editorconfig", ".editorconfig"]
COPY ["src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.DbMigrator/packages.lock.json", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]
COPY ["src/StarterApp.ServiceDefaults/packages.lock.json", "src/StarterApp.ServiceDefaults/"]

RUN dotnet restore "src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj"

COPY src/ src/

WORKDIR "/src/src/StarterApp.DbMigrator"
RUN dotnet publish "StarterApp.DbMigrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage — lightweight, no ODBC tools needed
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Drop root: run the migration job as the aspnet base image's predefined non-root user
# (UID 1654, via $APP_UID). DbUp only reads embedded scripts and connects to PostgreSQL,
# so it needs no write access or elevated privileges.
USER $APP_UID

ENTRYPOINT ["dotnet", "StarterApp.DbMigrator.dll"]
