import http from 'k6/http';
import { check, sleep } from 'k6';
import { AUTH_HEADERS, BASE_URL, ENDPOINTS, randomInt, randomItem } from './lib/config.js';
import {
  createCustomer,
  getCustomer,
  listCustomers,
} from './lib/customers.js';
import {
  createProduct,
  getProduct,
  listProducts,
} from './lib/products.js';
import {
  createOrder,
  getOrder,
  getOrdersByCustomer,
  getOrdersByStatus,
  updateOrderStatus,
} from './lib/orders.js';

const ORDER_STATUSES = ['Pending', 'Confirmed', 'Shipped', 'Delivered'];

// Expected statuses for the write-contention scenario: 409 Conflict is the
// optimistic-concurrency (xmin) outcome we are deliberately driving and MUST
// NOT count as an http_req_failed failure. Default k6 treats only 2xx/3xx as
// success, so without this a measured 409 would fail the error-rate threshold.
http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }, 409));

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 50 },
        { duration: '2m', target: 100 },
        { duration: '30s', target: 200 },
        { duration: '1m', target: 200 },
        { duration: '30s', target: 0 },
      ],
      exec: 'browseScenario',
    },
    orders: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 10 },
        { duration: '2m', target: 30 },
        { duration: '30s', target: 50 },
        { duration: '1m', target: 50 },
        { duration: '30s', target: 0 },
      ],
      exec: 'orderScenario',
    },
    // Low-VU scenario that repeatedly updates the SAME few product rows to
    // exercise the optimistic-concurrency (xmin -> 409) mapping under real
    // concurrency. Kept small and tagged so it does not dominate the latency
    // percentiles. Correctness is unit-tested; this measures the 409 path live.
    contention: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 5 },
        { duration: '2m', target: 8 },
        { duration: '1m', target: 8 },
        { duration: '30s', target: 0 },
      ],
      exec: 'contentionScenario',
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1500'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_CUSTOMERS}}`]: ['p(95)<300'],
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_PRODUCTS}}`]: ['p(95)<300'],
    // Deep-pagination LIMIT/OFFSET scans over the 20k seed are legitimately
    // slower than page 1, so the per-endpoint budget is looser.
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_CUSTOMERS_DEEP}}`]: ['p(95)<800'],
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_PRODUCTS_DEEP}}`]: ['p(95)<800'],
    [`http_req_duration{endpoint:${ENDPOINTS.ORDERS_BY_STATUS}}`]: ['p(95)<500'],
    [`http_req_duration{endpoint:${ENDPOINTS.CREATE_ORDER}}`]: ['p(95)<800'],
    [`http_req_duration{endpoint:${ENDPOINTS.GET_ORDER}}`]: ['p(95)<300'],
  },
};

// Page through the bulk-seeded owner-scoped catalog and collect ids. The seed
// rows are owned by the SAME k6 identity (perf-seed.sql / lib/config.js), so the
// standard list endpoints already return them — no special access needed.
function collectSeededIds(listFn, maxRows) {
  const ids = [];
  const pageSize = 100;
  const maxPages = Math.ceil(maxRows / pageSize);
  for (let page = 1; page <= maxPages; page++) {
    const res = listFn(page, pageSize);
    const rows = res.status === 200 ? res.json('data') || [] : [];
    for (const row of rows) {
      if (row && row.id) ids.push(row.id);
    }
    if (rows.length < pageSize) break;
  }
  return ids;
}

export function setup() {
  const customerIds = [];
  const productIds = [];

  for (let i = 0; i < 10; i++) {
    const suffix = `setup-${i}-${Date.now()}`;
    const customer = createCustomer(`Load Customer ${suffix}`, `load-${suffix}@test.com`);
    if (customer) customerIds.push(customer.id);

    const product = createProduct(
      `Load Product ${suffix}`,
      `Product for load testing ${i}`,
      10.0 + i * 5,
      'USD',
      100000,
    );
    if (product) productIds.push(product.id);
  }

  if (customerIds.length === 0 || productIds.length === 0) {
    throw new Error(
      `Setup failed: seeded customers=${customerIds.length}, products=${productIds.length}. ` +
        `Verify the API at ${__ENV.K6_BASE_URL || 'http://localhost:8080'} is reachable and accepting writes.`,
    );
  }

  // Surface the bulk-seeded population (20k customers / 20k products) to the VUs.
  // Without this, browse/order traffic only ever touches the ten setup-created
  // rows and the seeded volume is never read — the latency signal would ignore
  // the index/pagination paths the seed exists to exercise. Collecting ~500-1000
  // seeded ids widens randomItem()'s pool so get-by-id, orders-by-customer, and
  // order placement hit real seeded rows. The seeded orders are attached to
  // seeded customers, so getOrdersByCustomer over them now returns real rows.
  const seededCustomerIds = collectSeededIds(listCustomers, 1000);
  const seededProductIds = collectSeededIds(listProducts, 1000);
  for (const id of seededCustomerIds) customerIds.push(id);
  for (const id of seededProductIds) productIds.push(id);

  return { customerIds, productIds };
}

const BROWSE_ACTIONS = [
  (data) => listCustomers(1, 20),
  (data) => listProducts(1, 20),
  (data) => getCustomer(randomItem(data.customerIds)),
  (data) => getProduct(randomItem(data.productIds)),
  (data) => getOrdersByCustomer(randomItem(data.customerIds), 1, 20),
  // Deep pagination: random pages across the seeded range exercise large-OFFSET
  // LIMIT/OFFSET scans (the exact 20k-row cost). Tagged distinctly so its looser
  // p95 budget is measured separately from page-1 list latency.
  (data) => listCustomers(randomInt(1, 800), 20, ENDPOINTS.LIST_CUSTOMERS_DEEP),
  (data) => listProducts(randomInt(1, 800), 20, ENDPOINTS.LIST_PRODUCTS_DEEP),
  // Orders-by-status is the most index-sensitive seeded query (the seed spreads
  // four statuses across 20k orders). Previously smoke-only.
  (data) => getOrdersByStatus(randomItem(ORDER_STATUSES), 1, 20),
];

export function browseScenario(data) {
  randomItem(BROWSE_ACTIONS)(data);
  sleep(1);
}

export function orderScenario(data) {
  const customerId = randomItem(data.customerIds);
  const numItems = randomInt(1, 3);
  const items = [];
  for (let i = 0; i < numItems; i++) {
    items.push({
      productId: randomItem(data.productIds),
      quantity: randomInt(1, 3),
    });
  }

  const order = createOrder(customerId, items);
  if (order) {
    getOrder(order.id);
    updateOrderStatus(order.id, 'Confirmed');
  }

  sleep(1);
}

// Drive concurrent updates against a SMALL shared pool of products so multiple
// VUs collide on the same xmin row version and the 409 Conflict mapping is
// exercised under real load. Bounded VU count keeps this from dominating the
// percentiles; the 409 is registered as expected (response callback above) so
// it is not an http_req_failed failure. Correctness is unit-tested — this only
// confirms the 409 surfaces under concurrency.
const CONTENTION_PARAMS = {
  headers: Object.assign({}, AUTH_HEADERS, { 'Content-Type': 'application/json' }),
  tags: { endpoint: ENDPOINTS.UPDATE_PRODUCT },
};

export function contentionScenario(data) {
  // A handful of shared rows = high collision probability. Seeded products carry
  // 100k stock, so repeated stock writes never deplete them. We call the PUT
  // directly rather than via lib/updateProduct because that helper's strict
  // `status 204` check would register a failed check on the expected 409 and
  // sink the global checks-pass-rate threshold. Here 409 is a success outcome.
  const poolSize = Math.min(3, data.productIds.length);
  const targetId = data.productIds[randomInt(0, poolSize - 1)];

  const body = JSON.stringify({
    id: targetId,
    name: `Contention Product ${targetId}`,
    description: 'Updated under write contention',
    price: 10.0 + randomInt(0, 50),
    currency: 'USD',
    stock: 100000,
  });

  const res = http.put(`${BASE_URL}/api/v1/products/${targetId}`, body, CONTENTION_PARAMS);

  check(res, {
    'contention update: 204 or 409 conflict': (r) =>
      r.status === 204 || r.status === 409,
  });

  sleep(0.5);
}
