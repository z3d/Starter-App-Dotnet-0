FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY .editorconfig .
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

# NOTE on root: the API and DbMigrator images drop to the non-root $APP_UID user, but
# this Azure Functions base image deliberately stays on its default (root) user model.
# The image defines APP_UID=1654, but the Functions host writes to privileged paths it
# owns as root (e.g. /azure-functions-host/Secrets, log/data dirs), so switching to
# USER $APP_UID requires validated chowns of every host-writable directory and a full
# host-boot test — it is not a safe one-liner like the aspnet-based images. This worker
# is an internal Service-Bus subscriber (not internet-facing) run via AppHost for local
# integration tests. Harden to non-root as a validated deploy-time step if your platform
# (Kubernetes runAsNonRoot / OpenShift) requires it.
FROM --platform=linux/amd64 mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0@sha256:29a7a02c93694572710ebd4ccd64a8d6e5409455b32dd1f576caf26e301d2995
WORKDIR /home/site/wwwroot
# Baked-in default for the cleanup timer schedule (hourly). The TimerTrigger's
# %PayloadCapture:CleanupCron% lookup fails function indexing when the setting is absent,
# which would take down the Service Bus subscribers in the same worker; deployments override
# this env var to change the schedule.
ENV PayloadCapture__CleanupCron="0 0 * * * *"
COPY --from=build /app/publish .
