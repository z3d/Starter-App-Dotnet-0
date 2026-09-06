#!/usr/bin/env bash
# Deterministic runner regressions; no containers, API, or network required.
set -euo pipefail
DAST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT
mkdir -p "$TEST_DIR/dast" "$TEST_DIR/scripts/lib" "$TEST_DIR/bin"
cp "$DAST_DIR/run-dast.sh" "$DAST_DIR/automation.yaml" "$TEST_DIR/dast/"
if [[ -d "$DAST_DIR/lib" ]]; then cp -R "$DAST_DIR/lib" "$TEST_DIR/dast/"; fi
cp "$DAST_DIR/../scripts/lib/dev-idp.sh" "$TEST_DIR/scripts/lib/"
mkdir -p "$TEST_DIR/dast/reports"
cat > "$TEST_DIR/bin/container-stub" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
while [[ $# -gt 0 ]]; do
  if [[ "$1" == '-v' ]]; then
    shift
    work="${1%:/zap/wrk:rw}"
  fi
  shift
done
printf '{"site":[]}' > "$work/reports/dast-report.json"
printf 'Job openapi added 10 URLs\nJob spider found 10 URLs\n'
STUB
cat > "$TEST_DIR/bin/curl" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
method=GET
write_status=0
for arg in "$@"; do
  [[ "$arg" != DELETE ]] || method=DELETE
  [[ "$arg" != *http_code* ]] || write_status=1
  url="$arg"
done
if [[ "$url" == */orders/customer/* ]]; then
  printf '%s' "$PROBE_BODY"
  if [[ "$write_status" == 1 ]]; then printf '\n%s' "$PROBE_CODE"; fi
elif [[ "$method" == DELETE ]]; then printf '%s' "${PROBE_DELETE_CODE:-403}"
else printf '%s' "${PROBE_BYID_CODE:-404}"
fi
STUB
chmod +x "$TEST_DIR/bin/"*
export PATH="$TEST_DIR/bin:$PATH"
failures=0
check() {
  if "$@"; then printf 'PASS %s\n' "$*"; else
    printf 'FAIL %s\n' "$*" >&2
    failures=$((failures + 1))
  fi
}
render_target() {
  SKIP_BOOT=1 TARGET_URL="$1" DAST_TOKEN='test-token' CONTAINER_CLI=container-stub \
    bash "$TEST_DIR/dast/run-dast.sh" > "$TEST_DIR/runner.log" 2>&1
}
plan_contains_json() {
  local quoted
  quoted=$(jq -cn --arg value "$1" '$value')
  grep -Fq -- "$quoted" "$TEST_DIR/dast/reports/automation.rendered.yaml"
}
url_case() {
  render_target "$1" && plan_contains_json "$2" && plan_contains_json "$2/openapi/v1.json"
}
check url_case 'http://localhost:5164' 'http://host.docker.internal:5164'
check url_case 'https://review.invalid:8443/custompath/' 'https://review.invalid:8443/custompath'
check url_case 'https://review.invalid/custompath' 'https://review.invalid/custompath'
check url_case 'https://review.invalid/__DAST_AUTH_HEADER__/__ZAP_API_REGEX__' 'https://review.invalid/__DAST_AUTH_HEADER__/__ZAP_API_REGEX__'
check url_case 'http://127.0.0.2:5164/base' 'http://host.docker.internal:5164/base'
check url_case 'http://[::1]:5164/base' 'http://host.docker.internal:5164/base'
check url_case 'https://[2001:db8::1]/base' 'https://[2001:db8::1]/base'
check url_case 'https://review.invalid/a+b/(v1)/x&y' 'https://review.invalid/a+b/(v1)/x&y'
regex_case() {
  render_target 'https://review.invalid/a+b/(v1)/x&y' || return 1
  local expression
  expression=$(sed -n 's/^        url: //p' "$TEST_DIR/dast/reports/automation.rendered.yaml" | tail -1 | jq -r .) || return 1
  jq -en --arg expression "$expression" '
    ("https://review.invalid/a+b/(v1)/x&y/api/v1/products?page=1" | test($expression)) and
    ("https://reviewXinvalid/a+b/(v1)/x&y/api/v1/products" | test($expression) | not) and
    ("https://review.invalid/aab/v1/x&y/api/v1/products" | test($expression) | not)'
}
check regex_case
invalid_target() { if render_target "$1"; then return 1; fi; }
# A placeholder the renderer does not know must fail the render, never become YAML null.
unknown_placeholder() {
  printf 'url: __ZAP_TYPO_URL__\n' > "$TEST_DIR/typo.yaml"
  if (source "$TEST_DIR/dast/lib/plan.sh"; DAST_TOKEN=test-token dast_render_plan 'http://localhost:5164' "$TEST_DIR/typo.yaml" > "$TEST_DIR/typo.rendered" 2>/dev/null); then return 1; fi
  ! grep -q null "$TEST_DIR/typo.rendered"
}
check unknown_placeholder
for target in 'ftp://review.invalid' 'https://user:pass@review.invalid' 'https://review.invalid/base?x=1' 'https://review.invalid/#fragment' 'https://review.invalid:0' 'https://review.invalid:65536' 'https://review.invalid/a b'; do
  check invalid_target "$target"
done
# Execute the production probe independently of boot/scan, using controlled HTTP responses.
{
  printf 'set -euo pipefail\nSKIP_BOOT=0\nSKIP_SEED=0\nTARGET_URL=http://localhost\nDAST_TOKEN=test-token\n'
  printf 'log() { echo "$*"; }; err() { echo "$*" >&2; }\n'
  sed -n '/^# --- cross-owner IDOR/,$p' "$DAST_DIR/run-dast.sh"
} > "$TEST_DIR/probe.sh"
probe_case() {
  local expected="$1" status=0
  PROBE_CODE="$2" PROBE_BODY="$3" bash "$TEST_DIR/probe.sh" > "$TEST_DIR/probe.log" 2>&1 || status=$?
  if [[ "$expected" == pass ]]; then [[ "$status" == 0 ]]; else [[ "$status" != 0 ]]; fi
}
check probe_case pass 200 '{"data":[]}'
for code in 401 403 429 500; do
  check probe_case fail "$code" '{"type":"about:blank","title":"Error"}'
  check probe_case fail "$code" '{"data":[]}'
done
for body in '{}' '{"data":null}' '{"data":{}}' '{"data":""}' '{"data":[{"id":1}]}' 'malformed' ''; do
  check probe_case fail 200 "$body"
done
# The by-id and delete probes must fail on a leak too, not only pass on the stub's defaults:
# a 2xx is the leak itself, and any other code is a different bug the probe must not mask.
leak_case() {
  local variable="$1" code="$2" status=0
  env "$variable=$code" PROBE_CODE=200 PROBE_BODY='{"data":[]}' bash "$TEST_DIR/probe.sh" > "$TEST_DIR/probe.log" 2>&1 || status=$?
  [[ "$status" != 0 ]]
}
for code in 200 403 500; do check leak_case PROBE_BYID_CODE "$code"; done
for code in 204 404 500; do check leak_case PROBE_DELETE_CODE "$code"; done
printf '%s regression(s) failed\n' "$failures"
[[ "$failures" == 0 ]]
