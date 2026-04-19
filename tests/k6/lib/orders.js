import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, ENDPOINTS, jsonParams, tagParams } from './config.js';

const ORDERS_URL = `${BASE_URL}/api/v1/orders`;

export function createOrder(customerId, items) {
  const res = http.post(
    ORDERS_URL,
    JSON.stringify({ customerId, items }),
    jsonParams(ENDPOINTS.CREATE_ORDER),
  );
  check(res, { 'create order: status 201': (r) => r.status === 201 });
  return res.status === 201 ? res.json() : null;
}

export function getOrder(id) {
  const res = http.get(`${ORDERS_URL}/${id}`, tagParams(ENDPOINTS.GET_ORDER));
  check(res, { 'get order: status 200': (r) => r.status === 200 });
  return res;
}

export function getOrdersByCustomer(customerId, page = 1, pageSize = 50) {
  const res = http.get(
    `${ORDERS_URL}/customer/${customerId}?page=${page}&pageSize=${pageSize}`,
    tagParams(ENDPOINTS.ORDERS_BY_CUSTOMER),
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
    tagParams(ENDPOINTS.ORDERS_BY_STATUS),
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
    jsonParams(ENDPOINTS.UPDATE_ORDER_STATUS),
  );
  check(res, { 'update order status: status 200': (r) => r.status === 200 });
  return res.status === 200 ? res.json() : null;
}

export function cancelOrder(orderId) {
  const res = http.post(
    `${ORDERS_URL}/${orderId}/cancel`,
    null,
    tagParams(ENDPOINTS.CANCEL_ORDER),
  );
  check(res, { 'cancel order: status 200': (r) => r.status === 200 });
  return res.status === 200 ? res.json() : null;
}
