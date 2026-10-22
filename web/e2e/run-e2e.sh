#!/usr/bin/env bash
#
# Runs the web app's browser tests (web/e2e/tests, Playwright) against a stack of their own: a
# throwaway PostgreSQL, the dev Keycloak with the committed realm, the migrator, the API and the
# Vite dev server, each on a port no other rig uses. The suite is self-contained like the DAST and
# k6 harnesses; nothing here depends on a hosted service. With the API up, the generated client
# (src/lib/api.generated.ts) is regenerated from its OpenAPI document; in CI the run fails when the
# committed file differs, locally it says so, so the client never drifts from the API.
#
#   web/e2e/run-e2e.sh                       # boot, test, tear down
#   web/e2e/run-e2e.sh --grep "sign"         # arguments after the script go to playwright test
#   E2E_DEPLOYED=1 web/e2e/run-e2e.sh        # the same stack, but the nginx image (web/Dockerfile) in front, and e2e/deployed
#   web/e2e/run-e2e.sh --walkthrough         # boot the same stack and record web/e2e/walkthrough.mjs on it
#   SKIP_BOOT=1 BASE_URL=http://localhost:5173 API_URL=https://localhost:7286 IDP_URL=http://localhost:8090 web/e2e/run-e2e.sh
#                                            # against a rig already running (no client regeneration)
#   IDP_WAIT_SECONDS=360 API_WAIT_SECONDS=300 web/e2e/run-e2e.sh
#                                            # wait longer than the default two minutes each for Keycloak and the API
#
# Requirements: Docker or Podman, .NET 10 SDK, Node 24 and npm, curl. The Playwright browser
# comes from `npx playwright install chromium` (once).
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
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_DB="starterapp_e2e"
SKIP_BOOT="${SKIP_BOOT:-0}"
API_WAIT_SECONDS="${API_WAIT_SECONDS:-120}"
# The level the API names in its step-up challenge; the step-up test checks the app asks for it.
export STEP_UP_ACR="${STEP_UP_ACR:-urn:starterapp-dev:loa:mfa}"
RUN_ID="e2e-$$"
PG_CONTAINER="${RUN_ID}-pg"
IDP_CONTAINER="${RUN_ID}-idp"
WEB_CONTAINER="${RUN_ID}-web"
WEB_IMAGE="starterapp-web:${RUN_ID}"
API_PID=""
WEB_PID=""
# Not test-results: Playwright empties that folder when it starts, and a log written there is lost.
LOG_DIR="$WEB_DIR/e2e-logs"
API_LOG="$LOG_DIR/api.log"
MODE="tests"
if [[ "${1:-}" == "--walkthrough" ]]; then MODE="walkthrough"; shift; fi
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
  if [[ -n "$WEB_PID" ]] && kill -0 "$WEB_PID" 2>/dev/null; then
    kill "$WEB_PID" 2>/dev/null || true
    wait "$WEB_PID" 2>/dev/null || true
  fi
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

# The web image reaches the API on the host: Docker on Linux through the host gateway, so the API
# listens beyond loopback; Podman's host.containers.internal arrives on loopback.
start_api() {
  local bind=127.0.0.1
  [[ "$E2E_DEPLOYED" == "1" && "$CONTAINER_CLI" == "docker" ]] && bind=0.0.0.0
  log "Starting API on port $API_PORT (Development, validating tokens from the dev Keycloak${1:+, $1})"
  : > "$API_LOG"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://${bind}:${API_PORT}" \
  ConnectionStrings__database="$CONN" \
  RateLimiting__PermitLimit="${PERMIT_LIMIT:-1000000}" \
  Identity__Authority="${IDP_URL}/realms/starterapp" \
  Identity__RequireHttpsMetadata=false \
  Identity__StepUpAcrValues="$STEP_UP_ACR" \
    dotnet run --project "$REPO_ROOT/src/StarterApp.Api" -c Release --no-launch-profile >"$API_LOG" 2>&1 &
  API_PID=$!
  local ready=0 deadline=$((SECONDS + API_WAIT_SECONDS))
  while (( SECONDS < deadline )); do
    if curl -fsS "${API_URL}/health/ready" >/dev/null 2>&1 || curl -fsS "${API_URL}/alive" >/dev/null 2>&1; then ready=1; break; fi
    if ! kill -0 "$API_PID" 2>/dev/null; then err "API process exited early. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; fi
    sleep 2
  done
  [[ "$ready" == "1" ]] || { err "API did not become ready within ${API_WAIT_SECONDS}s (API_WAIT_SECONDS). Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; }
  log "API is ready."
}

if [[ "$SKIP_BOOT" != "1" ]]; then
  for tool in "$CONTAINER_CLI" dotnet curl npm; do
    command -v "$tool" >/dev/null 2>&1 || { err "Required tool '$tool' not found on PATH."; exit 2; }
  done
  mkdir -p "$LOG_DIR"
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
  idp_start "$IDP_CONTAINER" "$IDP_PORT" "$REPO_ROOT" || { err "Keycloak did not become ready within ${IDP_WAIT_SECONDS:-120}s (IDP_WAIT_SECONDS)."; exit 1; }

  log "Running database migrations (DbMigrator)"
  ConnectionStrings__database="$CONN" dotnet run --project "$REPO_ROOT/src/StarterApp.DbMigrator" -c Release

  # The specs sign in and write a handful of times; the deployed shape keeps the normal limit.
  if [[ "$E2E_DEPLOYED" == "1" ]]; then PERMIT_LIMIT="" start_api "the normal rate limit"; else start_api; fi

  if [[ "$MODE" == "tests" ]]; then
    log "Regenerating the API client from ${API_URL}/openapi/v1.json"
    (cd "$WEB_DIR" && OPENAPI_URL="${API_URL}/openapi/v1.json" npm run --silent gen:api)
    if ! git -C "$REPO_ROOT" diff --exit-code --quiet -- web/src/lib/api.generated.ts; then
      git -C "$REPO_ROOT" --no-pager diff --stat -- web/src/lib/api.generated.ts >&2
      if [[ "${CI:-}" == "true" ]]; then
        err "web/src/lib/api.generated.ts is not what the API serves. Commit the regenerated file."
        exit 1
      fi
      log "web/src/lib/api.generated.ts was regenerated from the API; commit it with this change."
    fi
  fi

  if [[ "$E2E_DEPLOYED" == "1" ]]; then
    log "Building the web image (web/Dockerfile)"
    "$CONTAINER_CLI" build -q -f "$WEB_DIR/Dockerfile" -t "$WEB_IMAGE" "$REPO_ROOT" >/dev/null
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
  log "SKIP_BOOT=1: testing the rig at ${BASE_URL:-http://localhost:5184} with the API at ${API_URL:-https://localhost:7286}"
fi

cd "$WEB_DIR"
if [[ "$MODE" == "walkthrough" ]]; then
  # The recording drives the dev server itself, so it is started here rather than by Playwright.
  if [[ -z "${BASE_URL:-}" ]]; then
    log "Starting the dev server on port $WEB_PORT"
    VITE_API_URL="$API_URL" VITE_OIDC_AUTHORITY="${IDP_URL}/realms/starterapp" npx vite --port "$WEB_PORT" --strictPort >"$LOG_DIR/vite.log" 2>&1 &
    WEB_PID=$!
    for _ in $(seq 1 60); do
      if curl -fsS "http://localhost:${WEB_PORT}" >/dev/null 2>&1; then break; fi
      sleep 1
    done
    export BASE_URL="http://localhost:${WEB_PORT}"
  fi
  log "Recording the walkthrough against $BASE_URL"
  node e2e/walkthrough.mjs "$@"
elif [[ "$E2E_DEPLOYED" == "1" ]]; then
  log "Running the browser tests against the web image"
  npx playwright test --project=deployed "$@"
else
  log "Running the browser tests"
  npx playwright test --project=chromium "$@"
fi
