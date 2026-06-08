FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6 AS build
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
RUN dotnet publish src/StarterApp.Functions/StarterApp.Functions.csproj -c Release -o /app/publish

# NOTE on root: the API and DbMigrator images drop to the non-root $APP_UID user, but
# this Azure Functions base image deliberately stays on its default (root) user model.
# The image defines APP_UID=1654, but the Functions host writes to privileged paths it
# owns as root (e.g. /azure-functions-host/Secrets, log/data dirs), so switching to
# USER $APP_UID requires validated chowns of every host-writable directory and a full
# host-boot test — it is not a safe one-liner like the aspnet-based images. This worker
# is an internal Service-Bus subscriber (not internet-facing) run via AppHost for local
# integration tests. Harden to non-root as a validated deploy-time step if your platform
# (Kubernetes runAsNonRoot / OpenShift) requires it.
FROM --platform=linux/amd64 mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0@sha256:03e965f15c2b1591db5e19ceb8c547f11b2e79a09f2959e0eb8ce1f61d01eb28
WORKDIR /home/site/wwwroot
COPY --from=build /app/publish .
