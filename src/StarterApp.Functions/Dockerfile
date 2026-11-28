FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
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
COPY src/StarterApp.Application/StarterApp.Application.csproj src/StarterApp.Application/
COPY src/StarterApp.Application/packages.lock.json src/StarterApp.Application/
COPY src/StarterApp.Domain/StarterApp.Domain.csproj src/StarterApp.Domain/
COPY src/StarterApp.Domain/packages.lock.json src/StarterApp.Domain/
COPY src/StarterApp.SharedKernel/StarterApp.SharedKernel.csproj src/StarterApp.SharedKernel/
COPY src/StarterApp.SharedKernel/packages.lock.json src/StarterApp.SharedKernel/
# --locked-mode: fail the build if the resolved graph diverges from packages.lock.json,
# matching CI, so the image ships exactly the audited dependency closure. Required here
# because the RestoreLockedMode MSBuild prop is gated on $(CI), unset inside docker build.
RUN dotnet restore src/StarterApp.Functions/StarterApp.Functions.csproj --locked-mode

# The worker runs the application's handlers in its own process, so it builds the application project, which embeds the migrator's scripts, and ships the API's appsettings.json.
COPY src/ src/
# Style is enforced by the format gate and the CI build on the canonical checkout; the image
# build only packages. Re-running IDE style analysis here makes the build depend on the
# checkout's line endings (a CRLF working tree fails IDE0055 under the pinned SDK while the
# same tree passes locally), so a Windows clone cannot build the image the E2E suite boots.
RUN dotnet publish src/StarterApp.Functions/StarterApp.Functions.csproj -c Release -o /app/publish -p:EnforceCodeStyleInBuild=false

# The digest is the linux/amd64 build itself, the only one published, not the index over it: an ARM host then needs no --platform, which a registry build that scans this file for base images cannot read.
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0@sha256:b3594abc530ed0d1d2b8da6b72cbfb2f8193c5faa08c7866674ff23e4cfdb8f1
# The base still ships apt 2.8.3 (CVE-2026-109518); drop this once its digest carries 2.8.4ubuntu0.1.
RUN apt-get update && apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0t64 && rm -rf /var/lib/apt/lists/*
# The base ships C build headers (libc6-dev pulls in linux-libc-dev, whose kernel CVEs fail the scan); nothing at runtime uses them.
RUN apt-get purge -y linux-libc-dev libc6-dev libc-dev-bin libcrypt-dev rpcsvc-proto
WORKDIR /home/site/wwwroot
# Baked-in default for the cleanup timer schedule (hourly). The TimerTrigger's
# %PayloadCapture:CleanupCron% lookup fails function indexing when the setting is absent,
# which would take down the Service Bus subscribers in the same worker; deployments override
# this env var to change the schedule.
ENV PayloadCapture__CleanupCron="0 0 * * * *"
ENV JobWatch__Cron="0 */15 * * * *"
ENV Idempotency__CleanupCron="0 30 3 * * *"
COPY --from=build /app/publish .
# Non-root like the API and migrator: the base image already gives $APP_UID /home and a world-writable
# Secrets directory, so the one change is listening above 1024.
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
