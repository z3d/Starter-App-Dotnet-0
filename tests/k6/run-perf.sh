#!/usr/bin/env bash
#
# Performance gate runner for the StarterApp API using k6.
#
# Boots a throwaway PostgreSQL, runs DbUp migrations, bulk-seeds owner-scoped
# data for the k6 gateway identity (tests/k6/seed/perf-seed.sql), starts the
# API in Development (GatewayIdentity:Mode=UnsignedDevelopment), then runs the
# k6 script against it. k6 exits non-zero on any threshold breach, which fails
# the build. The summary lands in tests/k6/reports/ for artifact upload.
#
# Usage:
#   tests/k6/run-perf.sh                          # full run: boot + seed + load.js
#   K6_SCRIPT=tests/k6/smoke.js tests/k6/run-perf.sh   # harness rehearsal with the smoke test
#   TARGET_URL=http://localhost:5164 SKIP_BOOT=1 tests/k6/run-perf.sh
#                                                 # run against an already-running instance
#
# Requirements: Docker, .NET 10 SDK, k6.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# --- configuration ------------------------------------------------------------
API_PORT="${API_PORT:-5165}"   # uncommon default (DAST uses 5164) to avoid foreign listeners
TARGET_URL="${TARGET_URL:-http://localhost:${API_PORT}}"
SKIP_BOOT="${SKIP_BOOT:-0}"               # 1 = use an existing instance, skip DB/API boot
SKIP_SEED="${SKIP_SEED:-0}"               # 1 = skip the bulk data seed
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_PORT="${PG_PORT:-55433}"               # distinct from the DAST runner's 55432
PG_DB="starterapp_perf"
K6_SCRIPT="${K6_SCRIPT:-$SCRIPT_DIR/load.js}"
# Volume floor for list-endpoint checks: with the bulk seed in place every list
# page must come back full. Unseeded runs (SKIP_SEED=1) drop the floor to 1
# unless the caller set it explicitly.
K6_MIN_LIST_ROWS_EXPLICIT="${K6_MIN_LIST_ROWS+1}"
K6_MIN_LIST_ROWS="${K6_MIN_LIST_ROWS:-20}"
REPORTS_DIR="$SCRIPT_DIR/reports"
SUMMARY_OUT="$REPORTS_DIR/summary.json"
RUN_ID="perf-$$"
PG_CONTAINER="${RUN_ID}-pg"
CONN="Host=localhost;Port=${PG_PORT};Database=${PG_DB};Username=postgres;Password=postgres"

API_PID=""
API_LOG="$REPORTS_DIR/api.log"

log()  { printf '\033[1;34m[perf]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[perf]\033[0m %s\n' "$*" >&2; }

cleanup() {
  local code=$?
  if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
    log "Stopping API (pid $API_PID)"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  if [[ "$SKIP_BOOT" != "1" ]]; then
    log "Removing PostgreSQL container"
    docker rm -f "$PG_CONTAINER" >/dev/null 2>&1 || true
  fi
  exit "$code"
}
trap cleanup EXIT INT TERM

# --- preflight ----------------------------------------------------------------
for tool in docker k6; do
  command -v "$tool" >/dev/null 2>&1 || { err "Required tool '$tool' not found on PATH."; exit 2; }
done
mkdir -p "$REPORTS_DIR"

# --- boot the target ----------------------------------------------------------
if [[ "$SKIP_BOOT" != "1" ]]; then
  command -v dotnet >/dev/null 2>&1 || { err "Required tool 'dotnet' not found on PATH."; exit 2; }

  # A foreign listener on the API port would answer the readiness probe and k6
  # would load-test the wrong service. Fail fast instead.
  if (exec 3<>"/dev/tcp/127.0.0.1/${API_PORT}") 2>/dev/null; then
    err "Port ${API_PORT} is already in use. Set API_PORT to a free port."
    exit 2
  fi

  log "Starting PostgreSQL ($PG_IMAGE) on port $PG_PORT"
  docker run -d --name "$PG_CONTAINER" \
    -e POSTGRES_PASSWORD=postgres \
    -e POSTGRES_DB="$PG_DB" \
    -p "${PG_PORT}:5432" \
    "$PG_IMAGE" >/dev/null

  log "Waiting for PostgreSQL to accept connections"
  for _ in $(seq 1 30); do
    if docker exec "$PG_CONTAINER" pg_isready -U postgres -d "$PG_DB" >/dev/null 2>&1; then break; fi
    sleep 1
  done

  log "Running database migrations (DbMigrator)"
  ConnectionStrings__database="$CONN" \
    dotnet run --project "$REPO_ROOT/src/StarterApp.DbMigrator" -c Release

  if [[ "$SKIP_SEED" != "1" ]]; then
    log "Bulk-seeding perf data (tests/k6/seed/perf-seed.sql)"
    docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -U postgres -d "$PG_DB" \
      < "$SCRIPT_DIR/seed/perf-seed.sql"
  else
    log "SKIP_SEED=1 — running without the bulk data seed"
    [[ -z "$K6_MIN_LIST_ROWS_EXPLICIT" ]] && K6_MIN_LIST_ROWS=1
  fi

  # The entire load profile runs under the single k6 gateway identity, so the
  # per-identity rate limit must be lifted or the gate measures the limiter
  # (98.6% 429s on the first nightly run), not the API.
  log "Starting API on port $API_PORT (Development / UnsignedDevelopment)"
  : > "$API_LOG"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://0.0.0.0:${API_PORT}" \
  ConnectionStrings__database="$CONN" \
  RateLimiting__PermitLimit=1000000 \
    dotnet run --project "$REPO_ROOT/src/StarterApp.Api" -c Release --no-launch-profile \
    >"$API_LOG" 2>&1 &
  API_PID=$!

  log "Waiting for API readiness at ${TARGET_URL}/health/ready"
  ready=0
  for _ in $(seq 1 60); do
    if curl -fsS "${TARGET_URL}/health/ready" >/dev/null 2>&1; then ready=1; break; fi
    if ! kill -0 "$API_PID" 2>/dev/null; then err "API process exited early. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; fi
    sleep 2
  done
  [[ "$ready" == "1" ]] || { err "API did not become ready. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; }
  log "API is ready."
else
  log "SKIP_BOOT=1 — targeting existing instance at $TARGET_URL"
fi

# --- run k6 -------------------------------------------------------------------
log "Running k6 ($K6_SCRIPT) against $TARGET_URL"
rm -f "$SUMMARY_OUT"

K6_BASE_URL="$TARGET_URL" \
K6_MIN_LIST_ROWS="$K6_MIN_LIST_ROWS" \
  k6 run --summary-export="$SUMMARY_OUT" "$K6_SCRIPT"

log "PERF PASSED: all thresholds held. Summary: $SUMMARY_OUT"
