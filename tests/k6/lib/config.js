export const BASE_URL = __ENV.K6_BASE_URL || 'http://localhost:8080';

export const AUTH_HEADERS = {
  'X-Authenticated-Subject': __ENV.K6_AUTH_SUBJECT || 'k6-user',
  'X-Authenticated-Principal-Type': 'User',
  'X-Authenticated-Tenant-Id': __ENV.K6_AUTH_TENANT || 'k6-tenant',
  'X-Authenticated-Scopes':
    'customers:read customers:write orders:read orders:write products:read products:write',
  'X-Authenticated-Amr': __ENV.K6_AUTH_AMR || 'mfa pwd',
};

export const JSON_HEADERS = {
  headers: Object.assign({}, AUTH_HEADERS, { 'Content-Type': 'application/json' }),
};

export const ENDPOINTS = {
  CREATE_CUSTOMER: 'create_customer',
  GET_CUSTOMER: 'get_customer',
  LIST_CUSTOMERS: 'list_customers',
  UPDATE_CUSTOMER: 'update_customer',
  DELETE_CUSTOMER: 'delete_customer',
  CREATE_PRODUCT: 'create_product',
  GET_PRODUCT: 'get_product',
  LIST_PRODUCTS: 'list_products',
  UPDATE_PRODUCT: 'update_product',
  DELETE_PRODUCT: 'delete_product',
  CREATE_ORDER: 'create_order',
  GET_ORDER: 'get_order',
  ORDERS_BY_CUSTOMER: 'orders_by_customer',
  ORDERS_BY_STATUS: 'orders_by_status',
  UPDATE_ORDER_STATUS: 'update_order_status',
  CANCEL_ORDER: 'cancel_order',
};

export function jsonParams(tag) {
  return {
    headers: Object.assign({}, AUTH_HEADERS, { 'Content-Type': 'application/json' }),
    tags: { endpoint: tag },
  };
}

export function tagParams(tag) {
  return { headers: AUTH_HEADERS, tags: { endpoint: tag } };
}

export function uniqueSuffix() {
  return `${__VU}-${__ITER}-${Date.now()}`;
}

export function randomItem(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

export function randomInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}
