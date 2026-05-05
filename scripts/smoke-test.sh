#!/bin/bash
set -euo pipefail

# Smoke test for StarterApp API — runs against a live deployment.
# Usage: ./scripts/smoke-test.sh [BASE_URL]
# Defaults to http://localhost:8080 (Docker Compose).
# For Aspire: ./scripts/smoke-test.sh https://localhost:7286

BASE_URL="${1:-http://localhost:8080}"
CURL_OPTS="-sf"
AUTH_HEADERS=(
    -H "X-Authenticated-Subject: smoke-test-user"
    -H "X-Authenticated-Principal-Type: User"
    -H "X-Authenticated-Tenant-Id: smoke-test-tenant"
    -H "X-Authenticated-Scopes: customers:read customers:write orders:read orders:write products:read products:write"
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

extract_id() {
    # Extract "id" from JSON — works with python3 or grep fallback
    if command -v python3 &>/dev/null; then
        python3 -c "import sys,json; print(json.load(sys.stdin)['id'])"
    else
        grep -o '"id":[0-9]*' | head -1 | grep -o '[0-9]*'
    fi
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
    -d "{\"orderId\":$CREATED_ORDER_ID,\"status\":\"Confirmed\"}"

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

# OrderId=0 on UpdateOrderStatus → 400
assert_status "OrderId=0 → 400" 400 PUT /api/v1/orders/0/status \
    -H "Content-Type: application/json" \
    -d '{"orderId":0,"status":"Confirmed"}'

# Invalid status enum → 400
assert_status "Invalid status enum → 400" 400 PUT \
    "/api/v1/orders/$CREATED_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":$CREATED_ORDER_ID,\"status\":\"Bogus\"}"

# --- Conflict Tests (409) ---

echo ""
echo "Conflicts"

# Invalid state transition (Confirmed → Delivered) → 409
assert_status "Invalid transition → 409" 409 PUT \
    "/api/v1/orders/$CREATED_ORDER_ID/status" \
    -H "Content-Type: application/json" \
    -d "{\"orderId\":$CREATED_ORDER_ID,\"status\":\"Delivered\"}"

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
