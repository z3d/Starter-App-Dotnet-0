import { sleep } from 'k6';
import { ENDPOINTS, randomInt, randomItem } from './lib/config.js';
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
  updateOrderStatus,
} from './lib/orders.js';

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
  },
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1500'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_CUSTOMERS}}`]: ['p(95)<300'],
    [`http_req_duration{endpoint:${ENDPOINTS.LIST_PRODUCTS}}`]: ['p(95)<300'],
    [`http_req_duration{endpoint:${ENDPOINTS.CREATE_ORDER}}`]: ['p(95)<800'],
    [`http_req_duration{endpoint:${ENDPOINTS.GET_ORDER}}`]: ['p(95)<300'],
  },
};

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

  return { customerIds, productIds };
}

const BROWSE_ACTIONS = [
  (data) => listCustomers(1, 20),
  (data) => listProducts(1, 20),
  (data) => getCustomer(randomItem(data.customerIds)),
  (data) => getProduct(randomItem(data.productIds)),
  (data) => getOrdersByCustomer(randomItem(data.customerIds), 1, 20),
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
