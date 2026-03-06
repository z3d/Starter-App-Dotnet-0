# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj", "src/StarterApp.DbMigrator/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]

RUN dotnet restore "src/StarterApp.DbMigrator/StarterApp.DbMigrator.csproj"

COPY src/ src/

WORKDIR "/src/src/StarterApp.DbMigrator"
RUN dotnet publish "StarterApp.DbMigrator.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage — lightweight, no ODBC tools needed
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "StarterApp.DbMigrator.dll"]
