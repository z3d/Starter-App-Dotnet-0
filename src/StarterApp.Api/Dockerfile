# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Directory.Build.props for shared configuration
COPY ["Directory.Build.props", "Directory.Build.props"]

# Copy csproj files first for better layer caching
COPY ["src/StarterApp.Api/StarterApp.Api.csproj", "src/StarterApp.Api/"]
COPY ["src/StarterApp.Domain/StarterApp.Domain.csproj", "src/StarterApp.Domain/"]
COPY ["src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj", "src/StarterApp.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/StarterApp.Api/StarterApp.Api.csproj"

# Copy the rest of the source code
COPY src/ src/

# Build the project
WORKDIR "/src/src/StarterApp.Api"
RUN dotnet build "StarterApp.Api.csproj" -c Release -o /app/build

# Publish the application
RUN dotnet publish "StarterApp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image — lightweight, no ODBC tools needed
# (The API uses Microsoft.Data.SqlClient, not ODBC. For sqlcmd debugging, use the db container.)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

# Start the application — migrations handled by the dedicated migrator service
ENTRYPOINT ["dotnet", "StarterApp.Api.dll"]