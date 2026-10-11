#!/usr/bin/env bash
#
# Runs the web app's browser tests (web/e2e/tests, Playwright) against a stack of their own: a
# throwaway PostgreSQL, the dev Keycloak with the committed realm, the migrator, the API and the
# Vite dev server, each on a port no other rig uses. The suite is self-contained like the DAST and
# k6 harnesses; nothing here depends on a hosted service.
#
#   web/e2e/run-e2e.sh                       # boot, test, tear down
#   web/e2e/run-e2e.sh --grep "step-up"      # arguments after the script go to playwright test
#   SKIP_BOOT=1 BASE_URL=http://localhost:5173 API_URL=http://localhost:5164 web/e2e/run-e2e.sh
#                                            # against a rig already running
#   E2E_DEPLOYED=1 web/e2e/run-e2e.sh        # the deployed shape instead (web/e2e/deployed): the
#                                            # production image (web/apps/web/Dockerfile) behind its
#                                            # own nginx, the API at its normal rate limit
#
# While the API is up, the committed OpenAPI document (web/packages/api-client/openapi.json, with no servers entry) is
# compared with the one the API serves, so a changed endpoint fails here until `pnpm export` has
# refreshed the generated client. CHECK_OPENAPI=0 skips it.
#
# Requirements: Docker or Podman, .NET 10 SDK, Node 22 and pnpm, python3, curl. The Playwright
# browser comes from `pnpm exec playwright install chromium` (once).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WEB_DIR="$REPO_ROOT/web"

API_PORT="${API_PORT:-5365}"
WEB_PORT="${WEB_PORT:-5184}"
PG_PORT="${PG_PORT:-15435}"
IDP_PORT="${IDP_PORT:-18083}"
DEPLOYED_PORT="${DEPLOYED_PORT:-18381}"
E2E_DEPLOYED="${E2E_DEPLOYED:-0}"
CHECK_OPENAPI="${CHECK_OPENAPI:-1}"
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_DB="starterapp_e2e"
SKIP_BOOT="${SKIP_BOOT:-0}"
RUN_ID="e2e-$$"
PG_CONTAINER="${RUN_ID}-pg"
IDP_CONTAINER="${RUN_ID}-idp"
WEB_CONTAINER="${RUN_ID}-web"
WEB_IMAGE="starterapp-web:${RUN_ID}"
# The level the API names in its step-up challenge; the step-up test checks the app asks for it.
export STEP_UP_ACR="${STEP_UP_ACR:-urn:starterapp-dev:loa:mfa}"
API_PID=""
API_LOG="$SCRIPT_DIR/results/api.log"
source "$REPO_ROOT/scripts/lib/dev-idp.sh"

CONTAINER_CLI="${CONTAINER_CLI:-}"
if [[ -z "$CONTAINER_CLI" ]]; then
  if command -v docker >/dev/null 2>&1; then CONTAINER_CLI=docker; else CONTAINER_CLI=podman; fi
fi
export CONTAINER_CLI

log()  { printf '\033[1;34m[e2e]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[e2e]\033[0m %s\n' "$*" >&2; }

cleanup() {
  local code=$?
  if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
    log "Stopping API (pid $API_PID)"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  if [[ "$SKIP_BOOT" != "1" ]]; then
    log "Removing the PostgreSQL and Keycloak containers"
    "$CONTAINER_CLI" rm -f "$PG_CONTAINER" "$IDP_CONTAINER" >/dev/null 2>&1 || true
  fi
  if [[ "$E2E_DEPLOYED" == "1" ]]; then
    "$CONTAINER_CLI" rm -f "$WEB_CONTAINER" >/dev/null 2>&1 || true
    "$CONTAINER_CLI" rmi "$WEB_IMAGE" >/dev/null 2>&1 || true
  fi
  exit "$code"
}
trap cleanup EXIT INT TERM

if [[ "$SKIP_BOOT" != "1" ]]; then
  for tool in "$CONTAINER_CLI" dotnet python3 curl pnpm; do
    command -v "$tool" >/dev/null 2>&1 || { err "Required tool '$tool' not found on PATH."; exit 2; }
  done
  mkdir -p "$SCRIPT_DIR/results"
  export API_URL="http://localhost:${API_PORT}"
  export IDP_URL="http://localhost:${IDP_PORT}"
  export WEB_PORT
  unset BASE_URL
  CONN="Host=localhost;Port=${PG_PORT};Database=${PG_DB};Username=postgres;Password=postgres"

  log "Starting PostgreSQL ($PG_IMAGE) on port $PG_PORT"
  "$CONTAINER_CLI" run -d --name "$PG_CONTAINER" -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB="$PG_DB" -p "${PG_PORT}:5432" "$PG_IMAGE" >/dev/null
  for _ in $(seq 1 30); do
    if "$CONTAINER_CLI" exec "$PG_CONTAINER" pg_isready -U postgres -d "$PG_DB" >/dev/null 2>&1; then break; fi
    sleep 1
  done

  log "Starting the dev Keycloak on port $IDP_PORT (the committed starterapp realm)"
  idp_start "$IDP_CONTAINER" "$IDP_PORT" "$REPO_ROOT" || { err "Keycloak did not become ready."; exit 1; }

  log "Running database migrations (DbMigrator)"
  ConnectionStrings__database="$CONN" dotnet run --project "$REPO_ROOT/src/StarterApp.DbMigrator" -c Release

  start_api() {
  log "Starting API on port $API_PORT (Development, validating tokens from the dev Keycloak${1:+, $1})"
  : > "$API_LOG"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://${API_BIND}:${API_PORT}" \
  ConnectionStrings__database="$CONN" \
  env ${PERMIT_LIMIT:+RateLimiting__PermitLimit=$PERMIT_LIMIT} \
  Identity__Authority="${IDP_URL}/realms/starterapp" \
  Identity__RequireHttpsMetadata=false \
  Identity__StepUpAcrValues="$STEP_UP_ACR" \
    dotnet run --project "$REPO_ROOT/src/StarterApp.Api" -c Release --no-launch-profile >"$API_LOG" 2>&1 &
  API_PID=$!

  ready=0
  for _ in $(seq 1 60); do
    if curl -fsS "${API_URL}/health/ready" >/dev/null 2>&1 || curl -fsS "${API_URL}/alive" >/dev/null 2>&1; then ready=1; break; fi
    if ! kill -0 "$API_PID" 2>/dev/null; then err "API process exited early. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; fi
    sleep 2
  done
  [[ "$ready" == "1" ]] || { err "API did not become ready. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; }
  log "API is ready."
  }
  # The web image reaches the API on the host: Docker on Linux through the host gateway, so the
  # API listens beyond loopback; Podman's host.containers.internal arrives on loopback.
  API_BIND=127.0.0.1
  [[ "$E2E_DEPLOYED" == "1" && "$CONTAINER_CLI" == "docker" ]] && API_BIND=0.0.0.0
  # The specs sign in and write a handful of times; the normal limit is kept for the deployed shape.
  PERMIT_LIMIT=1000000 start_api

  if [[ "$CHECK_OPENAPI" == "1" ]]; then
    log "Checking the committed OpenAPI document against the API's"
    curl -fsS "${API_URL}/openapi/v1.json" -o "$SCRIPT_DIR/results/openapi.json"
    if ! python3 - "$WEB_DIR/packages/api-client/openapi.json" "$SCRIPT_DIR/results/openapi.json" <<'EOF'
import json, sys
committed, served = (json.load(open(p)) for p in sys.argv[1:3])
served.pop("servers", None)
if committed != served:
    sys.exit(1)
EOF
    then
      err "web/packages/api-client/openapi.json is not what the API serves. From web/, run \`pnpm export\` against a running"
      err "API (OPENAPI_URL=${API_URL}/openapi/v1.json) and commit openapi.json and src/schema.d.ts."
      exit 1
    fi
  fi

  if [[ "$E2E_DEPLOYED" == "1" ]]; then
    log "Restarting the API at its normal rate limit"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
    PERMIT_LIMIT="" start_api "the normal rate limit"

    log "Building the web image (web/apps/web/Dockerfile)"
    "$CONTAINER_CLI" build -q -f "$WEB_DIR/apps/web/Dockerfile" -t "$WEB_IMAGE" "$REPO_ROOT" >/dev/null
    if [[ "$CONTAINER_CLI" == "docker" ]]; then upstream_host=host.docker.internal; add_host=(--add-host=host.docker.internal:host-gateway); else upstream_host=host.containers.internal; add_host=(); fi
    log "Starting the web image on port $DEPLOYED_PORT (nginx, the API at http://${upstream_host}:${API_PORT})"
    "$CONTAINER_CLI" run -d --name "$WEB_CONTAINER" ${add_host[@]+"${add_host[@]}"} -p "${DEPLOYED_PORT}:8080" \
      -e API_UPSTREAM="http://${upstream_host}:${API_PORT}" \
      -e OIDC_AUTHORITY="${IDP_URL}/realms/starterapp" \
      "$WEB_IMAGE" >/dev/null
    for _ in $(seq 1 30); do curl -fsS "http://localhost:${DEPLOYED_PORT}/healthz" >/dev/null 2>&1 && break; sleep 1; done
    curl -fsS "http://localhost:${DEPLOYED_PORT}/healthz" >/dev/null || { err "The web image did not answer. Its log:"; "$CONTAINER_CLI" logs "$WEB_CONTAINER" >&2; exit 1; }
    export BASE_URL="http://localhost:${DEPLOYED_PORT}"
  fi
else
  log "SKIP_BOOT=1: testing the rig at ${BASE_URL:-http://localhost:5184} with the API at ${API_URL:-http://localhost:5164}"
fi

log "Running the browser tests"
cd "$WEB_DIR"
if [[ "$E2E_DEPLOYED" == "1" ]]; then
  pnpm exec playwright test --project=deployed "$@"
else
  pnpm exec playwright test --project=chromium "$@"
fi
