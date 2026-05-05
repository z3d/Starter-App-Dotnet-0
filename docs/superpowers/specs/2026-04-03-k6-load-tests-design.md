# k6 Load Testing Design

## Context

The StarterApp API has comprehensive integration tests (xUnit + Testcontainers) and property-based fuzz tests (FsCheck), but no performance or load testing. Adding k6 tests provides two capabilities: fast smoke tests for CI pipelines that verify all endpoints respond correctly, and on-demand load tests that establish performance baselines and surface bottlenecks under sustained traffic.

## API Surface

Three resource groups, using the local gateway identity headers expected by the API when APIM is not present:

| Group | Base Path | Operations |
|-------|-----------|------------|
| Customers | `/api/v1/customers` | List (paginated), Get, Create, Update, Delete |
| Products | `/api/v1/products` | List (paginated), Get, Create, Update, Delete |
| Orders | `/api/v1/orders` | Get, By Customer, By Status, Create, Update Status, Cancel |
| Health | `/health`, `/health/ready`, `/health/live`, `/alive` | GET only |

Key constraints:
- Orders require valid customer and product IDs (FK validation)
- Customer emails must be unique
- Order creation checks product stock availability
- Deleting customers/products with order history returns 409

## File Structure

```
tests/k6/
├── smoke.js          # CI entry point: 1 VU, 1 iteration
├── load.js           # On-demand entry point: ramping VUs
├── lib/
│   ├── config.js     # BASE_URL, headers, uniqueSuffix()
│   ├── customers.js  # Customer API helpers (inline check() calls)
│   ├── products.js   # Product API helpers (inline check() calls)
│   └── orders.js     # Order API helpers (inline check() calls)
└── README.md         # Usage, env vars, thresholds
```

## Configuration (`lib/config.js`)

- `BASE_URL`: from `K6_BASE_URL` env var, defaults to `http://localhost:8080`
- `JSON_HEADERS`: `{ 'Content-Type': 'application/json' }`
- `uniqueSuffix()`: returns `${__VU}-${__ITER}-${Date.now()}` for collision-free test data

## Helper Modules

Each module (`lib/customers.js`, `lib/products.js`, `lib/orders.js`) exports functions that:
- Wrap HTTP calls with correct URL construction and payload shapes
- Run `check()` assertions on responses (status codes, body structure)
- Tag requests with endpoint names for per-endpoint threshold filtering
- Return parsed JSON bodies so callers can extract IDs

### Customer helpers
- `createCustomer(name, email)` -- POST, checks 201
- `getCustomer(id)` -- GET, checks 200
- `listCustomers(page, pageSize)` -- GET, checks 200 + data is array
- `updateCustomer(id, payload)` -- PUT, checks 204
- `deleteCustomer(id)` -- DELETE, checks 204

### Product helpers
- `createProduct(name, desc, price, currency, stock)` -- POST, checks 201
- `getProduct(id)` -- GET, checks 200
- `listProducts(page, pageSize)` -- GET, checks 200
- `updateProduct(id, payload)` -- PUT, checks 204
- `deleteProduct(id)` -- DELETE, checks 204

### Order helpers
- `createOrder(customerId, items)` -- POST, checks 201. Items: `[{productId, quantity}]`
- `getOrder(id)` -- GET, checks 200
- `getOrdersByCustomer(customerId, page, pageSize)` -- GET, checks 200
- `getOrdersByStatus(status, page, pageSize)` -- GET, checks 200
- `updateOrderStatus(orderId, status)` -- PUT, checks 200
- `cancelOrder(orderId)` -- POST, checks 200

## Smoke Test (`smoke.js`)

**Purpose**: CI gate. Verifies every endpoint responds correctly with valid data.

**Options**:
```javascript
{
  vus: 1,
  iterations: 1,
  thresholds: {
    checks: ['rate==1.00'],
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<2000'],
  },
}
```

**Flow** (sequential, grouped):
1. Health checks -- GET all four health endpoints, check 200
2. Create seed data -- one customer, one product
3. Customer CRUD -- list, get, update, verify
4. Product CRUD -- list, get, update, verify
5. Order lifecycle -- create → get → list by customer → list by status → update status to Confirmed → cancel
6. Negative cases -- GET nonexistent resources (404), POST invalid data (400)
7. Cleanup -- delete product and customer (tolerate 409 if order history prevents it)

## Load Test (`load.js`)

**Purpose**: Sustained traffic for baselines and bottleneck detection.

### Setup
`setup()` creates 10 customers and 10 products (stock: 100,000 each) and returns their IDs for all VUs to share.

### Scenarios

**Browse** (read-heavy):
```javascript
executor: 'ramping-vus',
stages: [
  { duration: '30s', target: 50 },   // ramp up
  { duration: '2m', target: 100 },   // steady state
  { duration: '30s', target: 200 },  // peak
  { duration: '1m', target: 200 },   // sustain peak
  { duration: '30s', target: 0 },    // ramp down
]
```
Randomly picks from: list customers, get customer, list products, get product, get orders by customer. 1s sleep between requests.

**Orders** (write-heavy):
```javascript
executor: 'ramping-vus',
stages: [
  { duration: '30s', target: 10 },
  { duration: '2m', target: 30 },
  { duration: '30s', target: 50 },
  { duration: '1m', target: 50 },
  { duration: '30s', target: 0 },
]
```
Each iteration: pick random customer + 1-3 random products, create order, get order, update status. 1s sleep.

### Thresholds
```javascript
{
  http_req_duration: ['p(95)<500', 'p(99)<1500'],
  http_req_failed: ['rate<0.01'],
  checks: ['rate>0.99'],
  'http_req_duration{endpoint:list_customers}': ['p(95)<300'],
  'http_req_duration{endpoint:list_products}': ['p(95)<300'],
  'http_req_duration{endpoint:create_order}': ['p(95)<800'],
  'http_req_duration{endpoint:get_order}': ['p(95)<300'],
}
```

## Running

```bash
# Smoke (CI default)
k6 run tests/k6/smoke.js

# Smoke against custom URL
K6_BASE_URL=https://localhost:7286 k6 run tests/k6/smoke.js

# Load test
K6_BASE_URL=http://localhost:8080 k6 run tests/k6/load.js
```

## Known Considerations

- **Stock depletion**: Seed products use 100,000 stock to avoid exhaustion during load tests
- **Email uniqueness**: `uniqueSuffix()` prevents collisions across VUs
- **Database growth**: Load tests create many orders; reset the test database between runs for consistent baselines
- **Delete constraints**: Smoke test tolerates 409 on cleanup when order history exists
- **Local gateway identity**: API auth remains gateway-owned; k6 sends the normalized `X-Authenticated-*` headers used by local `UnsignedDevelopment` mode

## Verification

1. Start the API via Docker Compose (`docker-compose up --build`) or Aspire
2. Run `k6 run tests/k6/smoke.js` -- all checks should pass, zero errors
3. Run `k6 run tests/k6/load.js` -- all thresholds should pass against a fresh database
4. Verify k6 output shows per-endpoint metrics via tags
