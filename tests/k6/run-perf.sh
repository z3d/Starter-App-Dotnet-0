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
SKIP_REDIS="${SKIP_REDIS:-0}"             # 1 = no Redis; by-id reads fall back to in-memory cache
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_PORT="${PG_PORT:-55433}"               # distinct from the DAST runner's 55432
PG_DB="starterapp_perf"
REDIS_IMAGE="${REDIS_IMAGE:-redis:7-alpine}"
REDIS_PORT="${REDIS_PORT:-56379}"         # distinct from PG_PORT; dedicated perf-Redis host port
K6_SCRIPT="${K6_SCRIPT:-$SCRIPT_DIR/load.js}"
# Volume floor for list-endpoint checks: with the bulk seed in place every list
# page must come back full. Unseeded runs (SKIP_SEED=1) drop the floor to 1
# unless the caller set it explicitly.
K6_MIN_LIST_ROWS_EXPLICIT="${K6_MIN_LIST_ROWS+1}"
K6_MIN_LIST_ROWS="${K6_MIN_LIST_ROWS:-20}"
REPORTS_DIR="$SCRIPT_DIR/reports"
SUMMARY_OUT="$REPORTS_DIR/summary.json"
# Run-over-run trend tracking. If a committed baseline exists, compare key
# percentiles after k6 exits. Warn-only by default so a noisy baseline cannot
# destabilize the gate; set REGRESSION_FAIL=1 to make a regression fatal.
BASELINE_FILE="${BASELINE_FILE:-$SCRIPT_DIR/baseline/summary-baseline.json}"
REGRESSION_PCT="${REGRESSION_PCT:-20}"    # % over baseline that counts as a regression
REGRESSION_FAIL="${REGRESSION_FAIL:-0}"   # 1 = exit non-zero on regression (default: warn)
RUN_ID="perf-$$"
PG_CONTAINER="${RUN_ID}-pg"
REDIS_CONTAINER="${RUN_ID}-redis"
CONN="Host=localhost;Port=${PG_PORT};Database=${PG_DB};Username=postgres;Password=postgres"
# StackExchange.Redis (via Aspire AddRedisDistributedCache("redis")) reads
# ConnectionStrings:redis as a host:port endpoint. The API only wires Redis when
# this connection string is present (Program.cs); otherwise it falls back to an
# in-process AddDistributedMemoryCache — which is what made the by-id latency
# signal measure an in-memory cache instead of a prod-like Redis round trip.
REDIS_CONN="localhost:${REDIS_PORT}"

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
    if [[ "$SKIP_REDIS" != "1" ]]; then
      log "Removing Redis container"
      docker rm -f "$REDIS_CONTAINER" >/dev/null 2>&1 || true
    fi
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

  if [[ "$SKIP_REDIS" != "1" ]]; then
    log "Starting Redis ($REDIS_IMAGE) on port $REDIS_PORT"
    docker run -d --name "$REDIS_CONTAINER" \
      -p "${REDIS_PORT}:6379" \
      "$REDIS_IMAGE" >/dev/null

    log "Waiting for Redis to accept connections"
    redis_ready=0
    for _ in $(seq 1 30); do
      if docker exec "$REDIS_CONTAINER" redis-cli ping 2>/dev/null | grep -q PONG; then redis_ready=1; break; fi
      sleep 1
    done
    [[ "$redis_ready" == "1" ]] || { err "Redis did not become ready."; exit 1; }
  else
    log "SKIP_REDIS=1 — no Redis; by-id reads fall back to the in-memory cache (by-id thresholds are not prod-meaningful)"
  fi

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
  # Pass Redis only when provisioned: an empty ConnectionStrings__redis still
  # trips Program.cs's IsNullOrEmpty check and stays on the in-memory fallback,
  # so SKIP_REDIS leaves the variable unset entirely. Built as an env array so
  # the empty (SKIP_REDIS) case adds nothing to the API process environment.
  REDIS_ENV=()
  if [[ "$SKIP_REDIS" != "1" ]]; then
    REDIS_ENV+=("ConnectionStrings__redis=$REDIS_CONN")
  fi

  log "Starting API on port $API_PORT (Development / UnsignedDevelopment)"
  : > "$API_LOG"
  env \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://0.0.0.0:${API_PORT}" \
  ConnectionStrings__database="$CONN" \
  RateLimiting__PermitLimit=1000000 \
  ${REDIS_ENV[@]+"${REDIS_ENV[@]}"} \
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

# --- baseline / trend comparison ----------------------------------------------
# Diff key percentiles against a committed known-good baseline so a slow drift
# that still passes the absolute thresholds is surfaced. Non-fatal by default.
# Refresh the baseline by copying a known-good run:
#   cp tests/k6/reports/summary.json tests/k6/baseline/summary-baseline.json
if [[ -f "$BASELINE_FILE" ]]; then
  if ! command -v jq >/dev/null 2>&1; then
    log "Baseline present but 'jq' not on PATH — skipping trend comparison."
  else
    log "Comparing against baseline: $BASELINE_FILE (regression threshold: +${REGRESSION_PCT}%)"
    regressed=0
    for metric in "p(95)" "p(99)"; do
      cur="$(jq -r --arg m "$metric" '.metrics.http_req_duration[$m] // empty' "$SUMMARY_OUT")"
      base="$(jq -r --arg m "$metric" '.metrics.http_req_duration[$m] // empty' "$BASELINE_FILE")"
      if [[ -z "$cur" || -z "$base" ]]; then
        log "  http_req_duration ${metric}: missing in current or baseline — skipped"
        continue
      fi
      # allowed = base * (1 + pct/100); compare with awk (floats)
      verdict="$(awk -v c="$cur" -v b="$base" -v p="$REGRESSION_PCT" \
        'BEGIN { allowed = b * (1 + p/100); if (b > 0 && c > allowed) print "REGRESS"; else print "OK" }')"
      delta="$(awk -v c="$cur" -v b="$base" 'BEGIN { if (b > 0) printf "%+.1f", (c-b)/b*100; else print "n/a" }')"
      if [[ "$verdict" == "REGRESS" ]]; then
        err "  http_req_duration ${metric}: ${cur}ms vs baseline ${base}ms (${delta}%) — REGRESSION (> +${REGRESSION_PCT}%)"
        regressed=1
      else
        log "  http_req_duration ${metric}: ${cur}ms vs baseline ${base}ms (${delta}%) — within budget"
      fi
    done
    if [[ "$regressed" == "1" ]]; then
      if [[ "$REGRESSION_FAIL" == "1" ]]; then
        err "Baseline regression detected and REGRESSION_FAIL=1 — failing the run."
        exit 1
      fi
      log "Baseline regression detected (warn-only; set REGRESSION_FAIL=1 to fail the run)."
    fi
  fi
else
  log "No baseline at $BASELINE_FILE — skipping trend comparison. To establish one, copy a known-good $SUMMARY_OUT there."
fi
