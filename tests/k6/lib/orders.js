import http from 'k6/http';
import { check } from 'k6';
import { AUTH_HEADERS, BASE_URL, JSON_HEADERS } from './config.js';

const ORDERS_URL = `${BASE_URL}/api/v1/orders`;

export function createOrder(customerId, items) {
  const res = http.post(
    ORDERS_URL,
    JSON.stringify({ customerId, items }),
    Object.assign({}, JSON_HEADERS, { tags: { endpoint: 'create_order' } }),
  );
  check(res, { 'create order: status 201': (r) => r.status === 201 });
  return res.status === 201 ? res.json() : null;
}

export function getOrder(id) {
  const res = http.get(`${ORDERS_URL}/${id}`, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'get_order' },
  });
  check(res, { 'get order: status 200': (r) => r.status === 200 });
  return res;
}

export function getOrdersByCustomer(customerId, page = 1, pageSize = 50) {
  const res = http.get(
    `${ORDERS_URL}/customer/${customerId}?page=${page}&pageSize=${pageSize}`,
    { headers: AUTH_HEADERS, tags: { endpoint: 'orders_by_customer' } },
  );
  check(res, {
    'orders by customer: status 200': (r) => r.status === 200,
    'orders by customer: data is array': (r) => Array.isArray(r.json('data')),
  });
  return res;
}

export function getOrdersByStatus(status, page = 1, pageSize = 50) {
  const res = http.get(
    `${ORDERS_URL}/status/${status}?page=${page}&pageSize=${pageSize}`,
    { headers: AUTH_HEADERS, tags: { endpoint: 'orders_by_status' } },
  );
  check(res, {
    'orders by status: status 200': (r) => r.status === 200,
    'orders by status: data is array': (r) => Array.isArray(r.json('data')),
  });
  return res;
}

export function updateOrderStatus(orderId, status) {
  const res = http.put(
    `${ORDERS_URL}/${orderId}/status`,
    JSON.stringify({ orderId, status }),
    Object.assign({}, JSON_HEADERS, {
      tags: { endpoint: 'update_order_status' },
    }),
  );
  check(res, { 'update order status: status 200': (r) => r.status === 200 });
  return res.status === 200 ? res.json() : null;
}

export function cancelOrder(orderId) {
  const res = http.post(`${ORDERS_URL}/${orderId}/cancel`, null, {
    headers: AUTH_HEADERS,
    tags: { endpoint: 'cancel_order' },
  });
  check(res, { 'cancel order: status 200': (r) => r.status === 200 });
  return res.status === 200 ? res.json() : null;
}
