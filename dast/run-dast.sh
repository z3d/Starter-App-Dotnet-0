#!/usr/bin/env bash
#
# DAST runner for the StarterApp API using OWASP ZAP.
#
# Boots a throwaway PostgreSQL, runs DbUp migrations, starts the API in
# Development (GatewayIdentity:Mode=UnsignedDevelopment), then runs the ZAP
# Automation Framework plan in dast/automation.yaml against it. Fails the
# build if any alert at or above the configured risk threshold is found.
#
# Usage:
#   dast/run-dast.sh                     # full self-contained run (boots DB + API)
#   FAIL_RISK=High dast/run-dast.sh      # only fail on High-risk alerts
#   TARGET_URL=http://localhost:5164 SKIP_BOOT=1 dast/run-dast.sh
#                                        # scan an already-running instance
#
# Requirements: Docker, .NET 10 SDK, jq.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# --- configuration ------------------------------------------------------------
API_PORT="${API_PORT:-5164}"
TARGET_URL="${TARGET_URL:-http://localhost:${API_PORT}}"
SKIP_BOOT="${SKIP_BOOT:-0}"               # 1 = scan an existing instance, skip DB/API boot
ZAP_IMAGE="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_PORT="${PG_PORT:-55432}"
PG_DB="starterapp_dast"
FAIL_RISK="${FAIL_RISK:-Medium}"          # Fail the run on alerts >= this risk (High|Medium|Low)
RUN_ID="dast-$$"
PG_CONTAINER="${RUN_ID}-pg"
CONN="Host=localhost;Port=${PG_PORT};Database=${PG_DB};Username=postgres;Password=postgres"

API_PID=""
API_LOG="$SCRIPT_DIR/reports/api.log"

log()  { printf '\033[1;34m[dast]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[dast]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[dast]\033[0m %s\n' "$*" >&2; }

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
for tool in docker jq; do
  command -v "$tool" >/dev/null 2>&1 || { err "Required tool '$tool' not found on PATH."; exit 2; }
done

# --- boot the target ----------------------------------------------------------
if [[ "$SKIP_BOOT" != "1" ]]; then
  command -v dotnet >/dev/null 2>&1 || { err "Required tool 'dotnet' not found on PATH."; exit 2; }

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

  log "Starting API on port $API_PORT (Development / UnsignedDevelopment)"
  : > "$API_LOG"
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:${API_PORT}" \
  ConnectionStrings__database="$CONN" \
    dotnet run --project "$REPO_ROOT/src/StarterApp.Api" -c Release --no-launch-profile \
    >"$API_LOG" 2>&1 &
  API_PID=$!

  log "Waiting for API readiness at ${TARGET_URL}/health/ready"
  ready=0
  for _ in $(seq 1 60); do
    if curl -fsS "${TARGET_URL}/health/ready" >/dev/null 2>&1 || curl -fsS "${TARGET_URL}/alive" >/dev/null 2>&1; then
      ready=1; break
    fi
    if ! kill -0 "$API_PID" 2>/dev/null; then err "API process exited early. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; fi
    sleep 2
  done
  [[ "$ready" == "1" ]] || { err "API did not become ready. Tail of $API_LOG:"; tail -n 40 "$API_LOG" >&2; exit 1; }
  log "API is ready."
else
  log "SKIP_BOOT=1 — scanning existing instance at $TARGET_URL"
fi

# --- run ZAP ------------------------------------------------------------------
log "Running OWASP ZAP ($ZAP_IMAGE)"
rm -f "$SCRIPT_DIR/reports/dast-report.json" "$SCRIPT_DIR/reports/dast-report.html"
chmod -R a+rwX "$SCRIPT_DIR/reports" 2>/dev/null || true

# --add-host makes host.docker.internal resolve to the host on Linux too.
docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -v "$SCRIPT_DIR:/zap/wrk:rw" \
  "$ZAP_IMAGE" \
  zap.sh -cmd -autorun /zap/wrk/automation.yaml || true

REPORT_JSON="$SCRIPT_DIR/reports/dast-report.json"
[[ -f "$REPORT_JSON" ]] || { err "ZAP produced no JSON report at $REPORT_JSON."; exit 1; }

# --- evaluate the gate --------------------------------------------------------
# ZAP riskcode: 3=High, 2=Medium, 1=Low, 0=Informational.
# ZAP confidence: 0=False Positive, 1=Low, 2=Medium, 3=High, 4=Confirmed.
# An alert suppressed via an `alertFilter` (newRisk: "False Positive") keeps its
# original riskcode but gets confidence "0" and zero instances. The gate must
# honour that classification, so we exclude confidence 0 and any 0-instance alert
# (a real finding always carries at least one instance). This keeps narrowly
# scoped false-positive suppressions (see automation.yaml) from failing the build
# while still failing on any genuine alert at or above the threshold.
case "$FAIL_RISK" in
  High)   THRESHOLD=3 ;;
  Medium) THRESHOLD=2 ;;
  Low)    THRESHOLD=1 ;;
  *) err "Invalid FAIL_RISK '$FAIL_RISK' (expected High|Medium|Low)"; exit 2 ;;
esac

log "Alert summary (HTML: dast/reports/dast-report.html):"
jq -r '
  def real: .confidence != "0" and ((.instances // []) | length) > 0;
  [.site[]?.alerts[]? | select(real)]
  | group_by(.riskcode)
  | map({risk: (.[0].riskdesc // "?"), count: length})[]
  | "  \(.risk): \(.count)"
' "$REPORT_JSON" 2>/dev/null || true

BREACHING=$(jq --argjson t "$THRESHOLD" '
  def real: .confidence != "0" and ((.instances // []) | length) > 0;
  [.site[]?.alerts[]? | select((.riskcode | tonumber) >= $t and real)] | length
' "$REPORT_JSON")

echo
if [[ "$BREACHING" -gt 0 ]]; then
  err "DAST FAILED: $BREACHING alert(s) at or above risk '$FAIL_RISK'."
  jq -r --argjson t "$THRESHOLD" '
    def real: .confidence != "0" and ((.instances // []) | length) > 0;
    .site[]?.alerts[]? | select((.riskcode | tonumber) >= $t and real)
    | "  [\(.riskdesc)] \(.name) — \(.instances | length) instance(s)"
  ' "$REPORT_JSON"
  exit 1
fi

log "DAST PASSED: no alerts at or above risk '$FAIL_RISK'."
