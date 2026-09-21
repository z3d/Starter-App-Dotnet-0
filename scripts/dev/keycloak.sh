#!/usr/bin/env bash
# Runs the dev Keycloak on its own, for a stack started without the AppHost (an API run with
# `dotnet run --project src/StarterApp.Api`). Same image and realm
# as the AppHost, same port (8090), so every tool finds it in one place.
#
#   scripts/dev/keycloak.sh          # start (or reuse) starterapp-dev-idp on :8090
#   scripts/dev/keycloak.sh stop
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
source "$ROOT/scripts/lib/dev-idp.sh"
CONTAINER_CLI="${CONTAINER_CLI:-$(command -v docker || command -v podman)}"
NAME=starterapp-dev-idp; PORT="${IDP_PORT:-8090}"
if [[ "${1:-}" == "stop" ]]; then "$CONTAINER_CLI" rm -f "$NAME" >/dev/null 2>&1 || true; echo "stopped $NAME"; exit 0; fi
if "$CONTAINER_CLI" ps --format '{{.Names}}' | grep -qx "$NAME"; then
  echo "$NAME already running on :$PORT"
elif "$CONTAINER_CLI" ps -a --format '{{.Names}}' | grep -qx "$NAME"; then
  "$CONTAINER_CLI" start "$NAME" >/dev/null; idp_wait "http://localhost:$PORT" && echo "$NAME started on :$PORT"
else
  idp_start "$NAME" "$PORT" "$ROOT" && echo "$NAME started on :$PORT (realm starterapp; admin console admin/admin)"
fi
