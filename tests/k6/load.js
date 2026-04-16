import { sleep } from 'k6';
import { uniqueSuffix } from './lib/config.js';
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
    'http_req_duration{endpoint:list_customers}': ['p(95)<300'],
    'http_req_duration{endpoint:list_products}': ['p(95)<300'],
    'http_req_duration{endpoint:create_order}': ['p(95)<800'],
    'http_req_duration{endpoint:get_order}': ['p(95)<300'],
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

function randomItem(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

export function browseScenario(data) {
  const actions = [
    () => listCustomers(1, 20),
    () => listProducts(1, 20),
    () => getCustomer(randomItem(data.customerIds)),
    () => getProduct(randomItem(data.productIds)),
    () => getOrdersByCustomer(randomItem(data.customerIds), 1, 20),
  ];

  const action = randomItem(actions);
  action();
  sleep(1);
}

export function orderScenario(data) {
  const customerId = randomItem(data.customerIds);
  const numItems = 1 + Math.floor(Math.random() * 3);
  const items = [];
  for (let i = 0; i < numItems; i++) {
    items.push({
      productId: randomItem(data.productIds),
      quantity: 1 + Math.floor(Math.random() * 3),
    });
  }

  const order = createOrder(customerId, items);
  if (order) {
    getOrder(order.id);
    updateOrderStatus(order.id, 'Confirmed');
  }

  sleep(1);
}
