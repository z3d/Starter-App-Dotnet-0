#!/bin/bash
set -euo pipefail

# Smoke test for StarterApp API — runs against a live deployment.
# Usage: ./scripts/smoke-test.sh [BASE_URL]
# For Aspire, pass the API URL shown in the dashboard.

BASE_URL="${1:-${SMOKE_BASE_URL:-}}"
if [ -z "$BASE_URL" ]; then
    echo "Usage: ./scripts/smoke-test.sh <BASE_URL>"
    echo "Example: ./scripts/smoke-test.sh https://localhost:7286"
    exit 2
fi

CURL_OPTS="-sf"
AUTH_HEADERS=(
    -H "X-Authenticated-Subject: smoke-test-user"
    -H "X-Authenticated-Principal-Type: User"
    -H "X-Authenticated-Tenant-Id: smoke-test-tenant"
    -H "X-Authenticated-Scopes: customers:read customers:write orders:read orders:write products:read products:write"
    -H "X-Authenticated-Amr: mfa"
)

# Allow self-signed certs for local HTTPS (Aspire dev certs)
if [[ "$BASE_URL" == https://* ]]; then
    CURL_OPTS="-sfk"
fi

PASS=0
FAIL=0
CREATED_PRODUCT_ID=""
CREATED_CUSTOMER_ID=""
CREATED_ORDER_ID=""
# Unique suffix to avoid collisions with previous runs
RUN_ID="$(date +%s)"

# --- Helpers ---

assert_status() {
    local description="$1" expected="$2" method="$3" url="$4"
    shift 4
    local assert_opts="-s"
    [[ "$BASE_URL" == https://* ]] && assert_opts="-sk"
    local actual
    actual=$(curl $assert_opts -o /dev/null -w "%{http_code}" -X "$method" "$BASE_URL$url" "${AUTH_HEADERS[@]}" "$@" 2>/dev/null || echo "000")
    if [ "$actual" = "$expected" ]; then
        echo "  PASS  $description ($actual)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL  $description (expected $expected, got $actual)"
        FAIL=$((FAIL + 1))
    fi
}

post_json() {
    local url="$1" body="$2"
    local post_opts="-s"
    [[ "$BASE_URL" == https://* ]] && post_opts="-sk"
    curl $post_opts -X POST "$BASE_URL$url" "${AUTH_HEADERS[@]}" -H "Content-Type: application/json" -d "$body" 2>/dev/null
}

# python3 must be PROVEN runnable, not merely on PATH: Windows ships a Store
# execution-alias stub named python3 that resolves under `command -v` but cannot run
# anything, so every JSON parse silently blanks. Probe it once with a real run and
# share the verdict; fall back to the grep parser when it is absent or the stub.
if python3 -c "print()" >/dev/null 2>&1; then JSON_VIA_PYTHON=1; else JSON_VIA_PYTHON=0; fi

extract_id() {
    # Extract "id" from JSON — works with python3 or grep fallback
    if [ "$JSON_VIA_PYTHON" = "1" ]; then
        python3 -c "import sys,json; print(json.load(sys.stdin)['id'])"
    else
        grep -o '"id":[0-9]*' | head -1 | grep -o '[0-9]*'
    fi
}

json_field() {
    # Extract a top-level string field from JSON on stdin — python3 or grep fallback.
    # Pass the field name as an argv parameter, never interpolated into the -c source, so a field
    # name can never be executed as Python.
    local field="$1"
    if [ "$JSON_VIA_PYTHON" = "1" ]; then
        python3 -c 'import sys,json; print(json.load(sys.stdin).get(sys.argv[1],""))' "$field"
    else
        grep -o "\"$field\":\"[^\"]*\"" | head -1 | cut -d'"' -f4
    fi
}

await_json_field() {
    # Poll a GET endpoint until a JSON field reaches the expected value. For state that is only
    # eventually visible (cache invalidation, projections, outbox-driven flows) a single-shot GET
    # races the propagation; polling absorbs benign delay while still failing on a real miss.
    local description="$1" url="$2" field="$3" expected="$4" timeout_seconds="${5:-30}"
    local get_opts="-s"
    [[ "$BASE_URL" == https://* ]] && get_opts="-sk"
    local deadline=$((SECONDS + timeout_seconds))
    local actual=""
    while [ $SECONDS -lt $deadline ]; do
        actual=$(curl $get_opts "$BASE_URL$url" "${AUTH_HEADERS[@]}" 2>/dev/null | json_field "$field" || echo "")
        if [ "$actual" = "$expected" ]; then
            echo "  PASS  $description ($field=$actual)"
            PASS=$((PASS + 1))
            return 0
        fi
        sleep 1
    done
    echo "  FAIL  $description (wanted $field=$expected, last saw '$actual' after ${timeout_seconds}s)"
    FAIL=$((FAIL + 1))
    return 0
}

# --- Health Check ---

echo ""
echo "Smoke testing: $BASE_URL"
echo "================================"

echo ""
echo "Health"
# Health check may return 503 under Aspire (service discovery probes) — warn but don't fail
health_opts="-s"
[[ "$BASE_URL" == https://* ]] && health_opts="-sk"
HEALTH_STATUS=$(curl $health_opts -o /dev/null -w "%{http_code}" "$BASE_URL/health" 2>/dev/null || echo "000")
if [ "$HEALTH_STATUS" = "200" ]; then
    echo "  PASS  GET /health ($HEALTH_STATUS)"
    PASS=$((PASS + 1))
else
    echo "  WARN  GET /health ($HEALTH_STATUS) — health check unhealthy, continuing with API tests"
fi

# --- Products ---

echo ""
echo "Products"
assert_status "GET /api/v1/products" 200 GET /api/v1/products

CREATED_PRODUCT_ID=$(post_json /api/v1/products \
    "{\"name\":\"SmokeTest Product $RUN_ID\",\"description\":\"Smoke test\",\"price\":25.00,\"currency\":\"USD\",\"stock\":100}" \
    | extract_id)

if [ -n "$CREATED_PRODUCT_ID" ] && [ "$CREATED_PRODUCT_ID" != "null" ]; then
    echo "  PASS  POST /api/v1/products (created id=$CREATED_PRODUCT_ID)"
    PASS=$((PASS + 1))
else
    echo "  FAIL  POST /api/v1/products (no id returned)"
    FAIL=$((FAIL + 1))
fi

assert_status "GET /api/v1/products/$CREATED_PRODUCT_ID" 200 GET "/api/v1/products/$CREATED_PRODUCT_ID"

# --- Customers ---

echo ""
echo "Customers"
assert_status "GET /api/v1/customers" 200 GET /api/v1/customers

CREATED_CUSTOMER_ID=$(post_json /api/v1/customers \
    "{\"name\":\"SmokeTest User $RUN_ID\",\"email\":\"smoke${RUN_ID}@example.com\"}" \
    | extract_id)

if [ -n "$CREATED_CUSTOMER_ID" ] && [ "$CREATED_CUSTOMER_ID" != "null" ]; then
    echo "  PASS  POST /api/v1/customers (created id=$CREATED_CUSTOMER_ID)"
    PASS=$((PASS + 1))
else
    echo "  FAIL  POST /api/v1/customers (no id returned)"
    FAIL=$((FAIL + 1))
fi

# --- Orders ---

echo ""
echo "Orders"

CREATED_ORDER_ID=$(post_json /api/v1/orders \
    "{\"customerId\":$CREATED_CUSTOMER_ID,\"items\":[{\"productId\":$CREATED_PRODUCT_ID,\"quantity\":2,\"unitPriceExcludingGst\":25.00,\"currency\":\"USD\",\"gstRate\":0.10}]}" \
    | extract_id)

if [ -n "$CREATED_ORDER_ID" ] && [ "$CREATED_ORDER_ID" != "null" ]; then
    echo "  PASS  POST /api/v1/orders (created id=$CREATED_ORDER_ID)"
    PASS=$((PASS + 1))
else
    echo "  FAIL  POST /api/v1/orders (no id returned)"
    FAIL=$((FAIL + 1))
fi

assert_status "GET /api/v1/orders/$CREATED_ORDER_ID" 200 GET "/api/v1/orders/$CREATED_ORDER_ID"
assert_status "GET /api/v1/orders/customer/$CREATED_CUSTOMER_ID" 200 GET "/api/v1/orders/customer/$CREATED_CUSTOMER_ID"
assert_status "GET /api/v1/orders/status/Pending" 200 GET /api/v1/orders/status/Pending

# Confirm the order (valid transition: Pending → Confirmed)
assert_status "PUT order status Pending→Confirmed" 200 PUT \
    "/api/v1/orders/$CREATED_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$CREATED_ORDER_ID\",\"status\":\"Confirmed\"}"

# The by-id read is cache-backed; poll rather than racing the invalidation with a one-shot GET
await_json_field "GET order reflects Confirmed" "/api/v1/orders/$CREATED_ORDER_ID" "status" "Confirmed" 30

# --- Validator Tests ---

echo ""
echo "Validators"

# Invalid email → 400
assert_status "Invalid email → 400" 400 POST /api/v1/customers \
    -H "Content-Type: application/json" \
    -d '{"name":"Bad","email":"not-valid"}'

# Email too long → 400
LONG_EMAIL=$(printf 'a%.0s' {1..321})
assert_status "Email too long → 400" 400 POST /api/v1/customers \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"Bad\",\"email\":\"${LONG_EMAIL}@x.com\"}"

# Empty currency on order item → 400
assert_status "Empty currency → 400" 400 POST /api/v1/orders \
    -H "Content-Type: application/json" \
    -d "{\"customerId\":$CREATED_CUSTOMER_ID,\"items\":[{\"productId\":$CREATED_PRODUCT_ID,\"quantity\":1,\"unitPriceExcludingGst\":5,\"currency\":\"\",\"gstRate\":0.10}]}"

# Invalid currency length → 400
assert_status "Bad currency length → 400" 400 POST /api/v1/orders \
    -H "Content-Type: application/json" \
    -d "{\"customerId\":$CREATED_CUSTOMER_ID,\"items\":[{\"productId\":$CREATED_PRODUCT_ID,\"quantity\":1,\"unitPriceExcludingGst\":5,\"currency\":\"ABCD\",\"gstRate\":0.10}]}"

# Empty OrderId on UpdateOrderStatus → 400
EMPTY_ORDER_ID="00000000-0000-0000-0000-000000000000"
assert_status "Empty OrderId → 400" 400 PUT "/api/v1/orders/$EMPTY_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$EMPTY_ORDER_ID\",\"status\":\"Confirmed\"}"

# Invalid status enum → 400
assert_status "Invalid status enum → 400" 400 PUT \
    "/api/v1/orders/$CREATED_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$CREATED_ORDER_ID\",\"status\":\"Bogus\"}"

# --- Conflict Tests (409) ---

echo ""
echo "Conflicts"

# Invalid state transition (Confirmed → Delivered) → 409
assert_status "Invalid transition → 409" 409 PUT \
    "/api/v1/orders/$CREATED_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$CREATED_ORDER_ID\",\"status\":\"Delivered\"}"

# Delete product with existing orders → 409
assert_status "Delete referenced product → 409" 409 DELETE \
    "/api/v1/products/$CREATED_PRODUCT_ID"

# Delete customer with existing orders → 409
assert_status "Delete customer with orders → 409" 409 DELETE \
    "/api/v1/customers/$CREATED_CUSTOMER_ID"

# --- Not Found Tests (404) ---

echo ""
echo "Not Found"
assert_status "GET nonexistent product → 404" 404 GET /api/v1/products/999999
assert_status "GET nonexistent customer → 404" 404 GET /api/v1/customers/999999
assert_status "GET nonexistent order → 404" 404 GET /api/v1/orders/999999

# --- Order Lifecycle ---

echo ""
echo "Order Lifecycle"

# Cancel the order (valid transition: Confirmed → Cancelled)
assert_status "Cancel order" 200 POST "/api/v1/orders/$CREATED_ORDER_ID/cancel"

# Cancel already-cancelled order → 409
assert_status "Cancel cancelled order → 409" 409 POST "/api/v1/orders/$CREATED_ORDER_ID/cancel"

# --- Summary ---

echo ""
echo "================================"
TOTAL=$((PASS + FAIL))
echo "Results: $PASS/$TOTAL passed, $FAIL failed"

if [ "$FAIL" -gt 0 ]; then
    echo "SMOKE TEST FAILED"
    exit 1
fi

echo "SMOKE TEST PASSED"
exit 0
