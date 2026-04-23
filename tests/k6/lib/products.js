import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, JSON_HEADERS } from './config.js';

const PRODUCTS_URL = `${BASE_URL}/api/v1/products`;

export function createProduct(name, description, price, currency, stock) {
  const res = http.post(
    PRODUCTS_URL,
    JSON.stringify({ name, description, price, currency, stock }),
    Object.assign({}, JSON_HEADERS, { tags: { endpoint: 'create_product' } }),
  );
  check(res, { 'create product: status 201': (r) => r.status === 201 });
  return res.status === 201 ? res.json() : null;
}

export function getProduct(id) {
  const res = http.get(`${PRODUCTS_URL}/${id}`, {
    tags: { endpoint: 'get_product' },
  });
  check(res, { 'get product: status 200': (r) => r.status === 200 });
  return res;
}

export function listProducts(page = 1, pageSize = 50) {
  const res = http.get(`${PRODUCTS_URL}?page=${page}&pageSize=${pageSize}`, {
    tags: { endpoint: 'list_products' },
  });
  check(res, {
    'list products: status 200': (r) => r.status === 200,
    'list products: data is array': (r) => Array.isArray(r.json('data')),
  });
  return res;
}

export function updateProduct(id, name, description, price, currency, stock) {
  const res = http.put(
    `${PRODUCTS_URL}/${id}`,
    JSON.stringify({ id, name, description, price, currency, stock }),
    Object.assign({}, JSON_HEADERS, { tags: { endpoint: 'update_product' } }),
  );
  check(res, { 'update product: status 204': (r) => r.status === 204 });
  return res;
}

export function deleteProduct(id) {
  const res = http.del(`${PRODUCTS_URL}/${id}`, null, {
    tags: { endpoint: 'delete_product' },
  });
  check(res, {
    'delete product: status 204 or 409': (r) =>
      r.status === 204 || r.status === 409,
  });
  return res;
}
