import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS } from './config.js';

const CUSTOMERS_URL = `${BASE_URL}/api/v1/customers`;

export function createCustomer(name, email) {
  const res = http.post(
    CUSTOMERS_URL,
    JSON.stringify({ name, email }),
    Object.assign({}, JSON_HEADERS, { tags: { endpoint: 'create_customer' } }),
  );
  check(res, { 'create customer: status 201': (r) => r.status === 201 });
  return res.status === 201 ? res.json() : null;
}

export function getCustomer(id) {
  const res = http.get(`${CUSTOMERS_URL}/${id}`, {
    tags: { endpoint: 'get_customer' },
  });
  check(res, { 'get customer: status 200': (r) => r.status === 200 });
  return res;
}

export function listCustomers(page = 1, pageSize = 50) {
  const res = http.get(`${CUSTOMERS_URL}?page=${page}&pageSize=${pageSize}`, {
    tags: { endpoint: 'list_customers' },
  });
  check(res, {
    'list customers: status 200': (r) => r.status === 200,
    'list customers: data is array': (r) => Array.isArray(r.json('data')),
  });
  return res;
}

export function updateCustomer(id, name, email) {
  const res = http.put(
    `${CUSTOMERS_URL}/${id}`,
    JSON.stringify({ id, name, email }),
    Object.assign({}, JSON_HEADERS, { tags: { endpoint: 'update_customer' } }),
  );
  check(res, { 'update customer: status 204': (r) => r.status === 204 });
  return res;
}

export function deleteCustomer(id) {
  const res = http.del(`${CUSTOMERS_URL}/${id}`, null, {
    tags: { endpoint: 'delete_customer' },
  });
  check(res, {
    'delete customer: status 204 or 409': (r) =>
      r.status === 204 || r.status === 409,
  });
  return res;
}
