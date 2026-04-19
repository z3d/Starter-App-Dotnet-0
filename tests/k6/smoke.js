import http from 'k6/http';
import { check, fail, group } from 'k6';
import { BASE_URL, uniqueSuffix } from './lib/config.js';
import {
  createCustomer,
  getCustomer,
  listCustomers,
  updateCustomer,
  deleteCustomer,
} from './lib/customers.js';
import {
  createProduct,
  getProduct,
  listProducts,
  updateProduct,
  deleteProduct,
} from './lib/products.js';
import {
  createOrder,
  getOrder,
  getOrdersByCustomer,
  getOrdersByStatus,
  updateOrderStatus,
  cancelOrder,
} from './lib/orders.js';

// `http_req_failed: ['rate==0']` is zero-tolerance. The Negative Cases and
// Cleanup groups use `http.setResponseCallback(http.expectedStatuses(...))`
// to mark 4xx/409 responses as expected so they do not count as failures.
// Each group restores the default callback before exiting.
export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    checks: ['rate==1.00'],
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<2000'],
  },
};

export default function () {
  const suffix = uniqueSuffix();
  let customerId, productId, orderId;

  group('Health Checks', () => {
    const endpoints = ['/health', '/health/ready', '/health/live', '/alive'];
    for (const ep of endpoints) {
      const res = http.get(`${BASE_URL}${ep}`);
      check(res, { [`${ep}: status 200`]: (r) => r.status === 200 });
    }
  });

  group('Customer CRUD', () => {
    const customer = createCustomer(
      `Smoke Customer ${suffix}`,
      `smoke-${suffix}@test.com`,
    );
    if (!check(customer, { 'customer created': (c) => c !== null })) {
      fail('customer creation failed; aborting iteration');
    }
    customerId = customer.id;

    listCustomers(1, 10);
    getCustomer(customerId);

    updateCustomer(
      customerId,
      `Updated Customer ${suffix}`,
      `updated-${suffix}@test.com`,
    );

    const updated = getCustomer(customerId);
    check(updated, {
      'customer name updated': (r) =>
        r.json('name') === `Updated Customer ${suffix}`,
    });
  });

  group('Product CRUD', () => {
    const product = createProduct(
      `Smoke Product ${suffix}`,
      'A test product for smoke testing',
      29.99,
      'USD',
      1000,
    );
    if (!check(product, { 'product created': (p) => p !== null })) {
      fail('product creation failed; aborting iteration');
    }
    productId = product.id;

    listProducts(1, 10);
    getProduct(productId);

    updateProduct(
      productId,
      `Updated Product ${suffix}`,
      'Updated description',
      39.99,
      'USD',
      500,
    );

    const updated = getProduct(productId);
    check(updated, {
      'product name updated': (r) =>
        r.json('name') === `Updated Product ${suffix}`,
    });
  });

  group('Order Lifecycle', () => {
    const order = createOrder(customerId, [
      { productId: productId, quantity: 2 },
    ]);
    if (!check(order, { 'order created': (o) => o !== null })) {
      fail('order creation failed; aborting iteration');
    }
    orderId = order.id;

    getOrder(orderId);
    getOrdersByCustomer(customerId);
    getOrdersByStatus('Pending');

    const confirmed = updateOrderStatus(orderId, 'Confirmed');
    check(confirmed, {
      'order confirmed': (o) => o !== null && o.status === 'Confirmed',
    });

    const cancelled = cancelOrder(orderId);
    check(cancelled, {
      'order cancelled': (o) => o !== null && o.status === 'Cancelled',
    });
  });

  group('Negative Cases', () => {
    http.setResponseCallback(http.expectedStatuses(400, 404));

    const notFoundCustomer = http.get(`${BASE_URL}/api/v1/customers/999999`);
    check(notFoundCustomer, {
      'nonexistent customer: 404': (r) => r.status === 404,
    });

    const notFoundProduct = http.get(`${BASE_URL}/api/v1/products/999999`);
    check(notFoundProduct, {
      'nonexistent product: 404': (r) => r.status === 404,
    });

    const notFoundOrder = http.get(`${BASE_URL}/api/v1/orders/999999`);
    check(notFoundOrder, {
      'nonexistent order: 404': (r) => r.status === 404,
    });

    const badCustomer = http.post(
      `${BASE_URL}/api/v1/customers`,
      JSON.stringify({ name: '', email: 'not-an-email' }),
      { headers: { 'Content-Type': 'application/json' } },
    );
    check(badCustomer, {
      'invalid customer: 400': (r) => r.status === 400,
    });

    http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }));
  });

  group('Cleanup', () => {
    // 409 is expected if order history prevents deletion
    http.setResponseCallback(http.expectedStatuses(204, 409));
    deleteProduct(productId);
    deleteCustomer(customerId);
    http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }));
  });
}
