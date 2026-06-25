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
# Port ZAP targets via host.docker.internal:<port>. Derive it from TARGET_URL so a
# SKIP_BOOT scan against a custom port stays in sync with the rendered plan; fall
# back to API_PORT when TARGET_URL carries no explicit numeric port. Strip the
# scheme and any path/query/fragment before reading the port so a trailing path
# (e.g. http://host:5164/api) doesn't defeat the parse.
ZAP_HOSTPORT="${TARGET_URL#*://}"   # drop scheme://
ZAP_HOSTPORT="${ZAP_HOSTPORT%%/*}"  # drop /path?query#frag
ZAP_PORT="${ZAP_HOSTPORT##*:}"      # port after last colon (whole string if none)
[[ "$ZAP_PORT" =~ ^[0-9]+$ ]] || ZAP_PORT="$API_PORT"
SKIP_BOOT="${SKIP_BOOT:-0}"               # 1 = scan an existing instance, skip DB/API boot
ZAP_IMAGE="${ZAP_IMAGE:-ghcr.io/zaproxy/zaproxy:stable}"
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
PG_PORT="${PG_PORT:-55432}"
PG_DB="starterapp_dast"
FAIL_RISK="${FAIL_RISK:-Medium}"          # Fail the run on alerts >= this risk (High|Medium|Low)
SKIP_SEED="${SKIP_SEED:-0}"               # 1 = skip the owner-scoped data seed
# Minimum-coverage floor: count of distinct URIs that must appear in the scan
# report. If ZAP hangs/OOMs/half-runs, the JSON exists but is nearly empty, so a
# 0-alert report would otherwise pass green. This floor (plus the ZAP exit-code
# guard below) turns a dead/throttled scan into a hard failure. It is the DAST
# analogue of the k6 gate's K6_MIN_LIST_ROWS volume floor.
DAST_MIN_URLS="${DAST_MIN_URLS:-5}"
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

  if [[ "$SKIP_SEED" != "1" ]]; then
    # Seed owner-scoped rows for the scanned identity (dast-user-01) plus a
    # second owner (dast-user-02) used by the cross-owner IDOR probe. An empty DB
    # makes every by-id/list probe return 404/empty, blinding response-differential
    # injection detection (see dast/seed/dast-seed.sql).
    log "Seeding owner-scoped DAST data (dast/seed/dast-seed.sql)"
    docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -U postgres -d "$PG_DB" \
      < "$SCRIPT_DIR/seed/dast-seed.sql"
  else
    log "SKIP_SEED=1 — running without the owner-scoped data seed"
  fi

  log "Starting API on port $API_PORT (Development / UnsignedDevelopment)"
  : > "$API_LOG"
  # Bind all interfaces (0.0.0.0), not just loopback: ZAP runs inside a Docker
  # container and reaches the API via host.docker.internal (the bridge gateway,
  # e.g. 172.17.0.1 on Linux runners). A localhost-only bind answers the host
  # readiness probe but refuses the container's connection. The readiness curl
  # still uses TARGET_URL (localhost), which 0.0.0.0 also serves.
  # Lift the per-identity rate limit. The replacer in automation.yaml injects a
  # SINGLE identity (dast-user-01) on every request, so the whole ~20-min active
  # scan shares one per-identity bucket. At the default RateLimiting:PermitLimit=100
  # per 60s window, everything after ~100 requests is 429'd before reaching a
  # handler, gutting active-scan coverage. The k6 perf gate lifts it the same way
  # (tests/k6/run-perf.sh) for the same single-identity reason.
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

# Render the automation template with the real port (single source of truth lives
# here, not in the YAML). The rendered plan lands under the git-ignored reports dir.
RENDERED_PLAN="$SCRIPT_DIR/reports/automation.rendered.yaml"
sed "s/__API_PORT__/${ZAP_PORT}/g" "$SCRIPT_DIR/automation.yaml" > "$RENDERED_PLAN"

# --add-host makes host.docker.internal resolve to the host on Linux too.
# Capture ZAP's exit code instead of swallowing it with `|| true`. ZAP autorun
# exit codes: 0 = success / no plan failures, 1 = at least one FAIL (the plan
# sets parameters.failOnError: true, so a job error surfaces here), 2 = at least
# one WARN, 3 = command-line error. A hang/OOM/crash inside the container also
# yields a non-zero/non-2 code. We must briefly disable `set -e` around the run
# so the non-zero code is captured rather than aborting the script before the
# guards below can produce a clear message.
set +e
docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -v "$SCRIPT_DIR:/zap/wrk:rw" \
  "$ZAP_IMAGE" \
  zap.sh -cmd -autorun /zap/wrk/reports/automation.rendered.yaml
ZAP_EXIT=$?
set -e

# Treat anything other than 0 (clean) or 2 (WARN — e.g. non-failing passive
# warnings) as a hard infrastructure/plan failure. The normal "alerts found"
# path still exits 0 from ZAP (alerts are reported, not plan failures), so the
# JSON gate below remains the authority on findings. This catches the dead/
# throttled/half-run scenario that a trailing `|| true` would have passed green.
if [[ "$ZAP_EXIT" != "0" && "$ZAP_EXIT" != "2" ]]; then
  err "ZAP exited with code $ZAP_EXIT (expected 0 or 2). The scan likely failed to run, hung, or crashed — not a clean 0-alert result."
  exit 1
fi

REPORT_JSON="$SCRIPT_DIR/reports/dast-report.json"
[[ -f "$REPORT_JSON" ]] || { err "ZAP produced no JSON report at $REPORT_JSON."; exit 1; }

# Coverage floor. The traditional-json report has no request/message counter, so
# the most reliable signal that the scan actually reached the app is the number
# of distinct URIs ZAP recorded across alert instances. A real scan of the seeded
# /api/v1 surface plus the static doc routes always produces well above the
# floor; a scan that never connected (or was 429'd into oblivion) produces few or
# none. Defensive jq (`// empty`) tolerates a report with no sites/alerts. This
# is the DAST analogue of the k6 gate's K6_MIN_LIST_ROWS volume floor.
SCANNED_URLS=$(jq -r '
  [.site[]?.alerts[]?.instances[]?.uri // empty] | unique | length
' "$REPORT_JSON" 2>/dev/null || echo 0)
[[ "$SCANNED_URLS" =~ ^[0-9]+$ ]] || SCANNED_URLS=0
log "Distinct URIs in scan report: $SCANNED_URLS (floor: $DAST_MIN_URLS)"
if [[ "$SCANNED_URLS" -lt "$DAST_MIN_URLS" ]]; then
  err "DAST FAILED: only $SCANNED_URLS distinct URI(s) in the report, below the floor of $DAST_MIN_URLS."
  err "The scan likely did not reach the app (boot failure, wrong port, or fully rate-limited). Inspect dast/reports/api.log and dast-report.html."
  exit 1
fi

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

# --- cross-owner IDOR / owner-scope regression probe --------------------------
# The ZAP scan injects a SINGLE fully-authorized identity (dast-user-01), so a
# handler that dropped its IOwnerOnlyPolicy check is structurally invisible to it
# — every request it makes is in-scope by construction. This scripted probe fills
# that gap directly: dast-seed.sql planted owner-02 resources at fixed, known ids;
# here we ask for them while presenting OWNER-01's identity headers (the same set
# automation.yaml injects) and assert the API hides or forbids them.
#
# Expected (owner-scoping intact):
#   * cross-owner GET by-id        -> 404 (read hidden as not-found)
#   * cross-owner GET list-by-fk   -> 200 with an empty data array (owner-filtered)
#   * cross-owner DELETE (mutation)-> 403 (owner policy rejects on a loaded row)
# Any other outcome (200 with the row, a 2xx delete) is an IDOR leak -> fail.
#
# Only runs in the boot+seed path: SKIP_BOOT means we don't know the target's
# data, and SKIP_SEED means the owner-02 fixtures are absent.
if [[ "$SKIP_BOOT" != "1" && "$SKIP_SEED" != "1" ]]; then
  echo
  log "Running cross-owner IDOR probe (owner-01 identity vs owner-02 resources)"

  # Owner-02 fixtures (fixed ids from dast/seed/dast-seed.sql).
  XO_CUSTOMER_ID=900001
  XO_PRODUCT_ID=900001
  XO_ORDER_GUID="00000000-0000-0000-0000-0000dad70002"

  # Owner-01 identity headers — must match automation.yaml's replacer rules.
  idem_headers=(
    -H "X-Authenticated-Subject: dast-user-01"
    -H "X-Authenticated-Principal-Type: User"
    -H "X-Authenticated-Tenant-Id: dast-tenant-01"
    -H "X-Authenticated-Scopes: customers:read customers:write orders:read orders:write products:read products:write"
    -H "X-Authenticated-Amr: mfa pwd"
  )

  idor_fail=0

  # Returns the HTTP status code for a request as owner-01.
  xo_status() {
    local method="$1" path="$2"
    curl -s -o /dev/null -w '%{http_code}' -X "$method" "${idem_headers[@]}" "${TARGET_URL}${path}"
  }
  # Returns the response body for a GET as owner-01.
  xo_body() {
    local path="$1"
    curl -s "${idem_headers[@]}" "${TARGET_URL}${path}"
  }

  # 1. Cross-owner GET customer by id -> must be 404 (read hidden).
  code=$(xo_status GET "/api/v1/customers/${XO_CUSTOMER_ID}")
  if [[ "$code" == "404" ]]; then
    log "  OK customer/${XO_CUSTOMER_ID} -> 404 (hidden)"
  else
    err "  IDOR customer/${XO_CUSTOMER_ID} -> $code (expected 404 — owner-02's customer leaked to owner-01)"
    idor_fail=1
  fi

  # 2. Cross-owner GET product by id -> must be 404 (read hidden).
  code=$(xo_status GET "/api/v1/products/${XO_PRODUCT_ID}")
  if [[ "$code" == "404" ]]; then
    log "  OK product/${XO_PRODUCT_ID} -> 404 (hidden)"
  else
    err "  IDOR product/${XO_PRODUCT_ID} -> $code (expected 404 — owner-02's product leaked to owner-01)"
    idor_fail=1
  fi

  # 3. Cross-owner GET order by id -> must be 404 (read hidden).
  code=$(xo_status GET "/api/v1/orders/${XO_ORDER_GUID}")
  if [[ "$code" == "404" ]]; then
    log "  OK orders/${XO_ORDER_GUID} -> 404 (hidden)"
  else
    err "  IDOR orders/${XO_ORDER_GUID} -> $code (expected 404 — owner-02's order leaked to owner-01)"
    idor_fail=1
  fi

  # 4. Cross-owner list orders-by-customer (owner-02's customer id) -> 200 with an
  #    empty data array. The list path filters by owner, so the foreign customer's
  #    orders must never appear. A non-empty data array is a leak.
  body=$(xo_body "/api/v1/orders/customer/${XO_CUSTOMER_ID}")
  rows=$(printf '%s' "$body" | jq -r '(.data // []) | length' 2>/dev/null || echo "parse-error")
  if [[ "$rows" == "0" ]]; then
    log "  OK orders/customer/${XO_CUSTOMER_ID} -> empty list (owner-filtered)"
  else
    err "  IDOR orders/customer/${XO_CUSTOMER_ID} -> $rows row(s) (expected 0 — owner-02's orders leaked to owner-01)"
    idor_fail=1
  fi

  # 5. Cross-owner DELETE customer (mutation) -> must be 403. The handler loads the
  #    row then calls IOwnerOnlyPolicy.Authorize, which throws ForbiddenAccessException
  #    (mapped to 403) for a foreign owner. A 2xx here means the mutation went through.
  code=$(xo_status DELETE "/api/v1/customers/${XO_CUSTOMER_ID}")
  if [[ "$code" == "403" ]]; then
    log "  OK DELETE customer/${XO_CUSTOMER_ID} -> 403 (forbidden)"
  else
    err "  IDOR DELETE customer/${XO_CUSTOMER_ID} -> $code (expected 403 — cross-owner mutation not blocked)"
    idor_fail=1
  fi

  # 6. Cross-owner DELETE product (mutation) -> must be 403 (same rationale).
  code=$(xo_status DELETE "/api/v1/products/${XO_PRODUCT_ID}")
  if [[ "$code" == "403" ]]; then
    log "  OK DELETE product/${XO_PRODUCT_ID} -> 403 (forbidden)"
  else
    err "  IDOR DELETE product/${XO_PRODUCT_ID} -> $code (expected 403 — cross-owner mutation not blocked)"
    idor_fail=1
  fi

  echo
  if [[ "$idor_fail" != "0" ]]; then
    err "DAST FAILED: cross-owner IDOR probe detected an owner-scope regression (see above)."
    exit 1
  fi
  log "IDOR PROBE PASSED: owner-02 resources stay hidden/forbidden under owner-01 identity."
else
  log "Skipping cross-owner IDOR probe (requires the boot+seed path; SKIP_BOOT/SKIP_SEED set)."
fi
