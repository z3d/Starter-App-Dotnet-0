FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY .editorconfig .
COPY BannedSymbols.txt .
COPY NuGet.config .
COPY src/StarterApp.Functions/StarterApp.Functions.csproj src/StarterApp.Functions/
COPY src/StarterApp.Functions/packages.lock.json src/StarterApp.Functions/
COPY src/StarterApp.ServiceDefaults/StarterApp.ServiceDefaults.csproj src/StarterApp.ServiceDefaults/
COPY src/StarterApp.ServiceDefaults/packages.lock.json src/StarterApp.ServiceDefaults/
# --locked-mode: fail the build if the resolved graph diverges from packages.lock.json,
# matching CI, so the image ships exactly the audited dependency closure. Required here
# because the RestoreLockedMode MSBuild prop is gated on $(CI), unset inside docker build.
RUN dotnet restore src/StarterApp.Functions/StarterApp.Functions.csproj --locked-mode

COPY src/StarterApp.Functions/ src/StarterApp.Functions/
COPY src/StarterApp.ServiceDefaults/ src/StarterApp.ServiceDefaults/
# Style is enforced by the format gate and the CI build on the canonical checkout; the image
# build only packages. Re-running IDE style analysis here makes the build depend on the
# checkout's line endings (a CRLF working tree fails IDE0055 under the pinned SDK while the
# same tree passes locally), so a Windows clone cannot build the image the E2E suite boots.
RUN dotnet publish src/StarterApp.Functions/StarterApp.Functions.csproj -c Release -o /app/publish -p:EnforceCodeStyleInBuild=false

FROM --platform=linux/amd64 mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0@sha256:6990da543e1dc1c7bb8d8d32c702c87a525db7b8ea621e645b479c822ed72c6d
# The base still ships apt 2.8.3 (CVE-2026-109518); drop this once its digest carries 2.8.4ubuntu0.1.
RUN apt-get update && apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 && rm -rf /var/lib/apt/lists/*
WORKDIR /home/site/wwwroot
# Baked-in default for the cleanup timer schedule (hourly). The TimerTrigger's
# %PayloadCapture:CleanupCron% lookup fails function indexing when the setting is absent,
# which would take down the Service Bus subscribers in the same worker; deployments override
# this env var to change the schedule.
ENV PayloadCapture__CleanupCron="0 0 * * * *"
ENV JobWatch__Cron="0 */15 * * * *"
COPY --from=build /app/publish .
# Non-root like the API and migrator: the base image already gives $APP_UID /home and a world-writable
# Secrets directory, so the one change is listening above 1024.
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
