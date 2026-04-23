FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/StarterApp.Functions/StarterApp.Functions.csproj src/StarterApp.Functions/
COPY src/StarterApp.Functions/packages.lock.json src/StarterApp.Functions/
COPY src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj src/StarterApp.ServiceDefaults/
COPY src/StarterApp.ServiceDefaults/packages.lock.json src/StarterApp.ServiceDefaults/
RUN dotnet restore src/StarterApp.Functions/StarterApp.Functions.csproj

COPY src/StarterApp.Functions/ src/StarterApp.Functions/
COPY src/StarterApp.ServiceDefaults/ src/StarterApp.ServiceDefaults/
RUN dotnet publish src/StarterApp.Functions/StarterApp.Functions.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
WORKDIR /home/site/wwwroot
COPY --from=build /app/publish .
