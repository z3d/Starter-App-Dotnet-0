export const BASE_URL = __ENV.K6_BASE_URL || 'http://localhost:8080';

// Volume floor for list-endpoint checks: a fast-but-empty response must not
// pass. Defaults to 1 (non-empty) so unseeded local runs stay green; the CI
// perf gate seeds bulk data and sets 20 so every requested page comes back
// full (see tests/k6/run-perf.sh and tests/k6/seed/perf-seed.sql).
export const MIN_LIST_ROWS = Math.max(1, parseInt(__ENV.K6_MIN_LIST_ROWS || '1', 10) || 1);

// OIDC access token for the whole load profile — minted by the runner script
// (tests/k6/run-perf.sh boots the dev Keycloak and does a password grant as
// k6-user, whose sub/tid match the perf seed's owner columns). Missing token
// means every request 401s, which the checks surface immediately.
export const AUTH_HEADERS = {
  Authorization: `Bearer ${__ENV.K6_AUTH_TOKEN || ''}`,
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
  LIST_CUSTOMERS_DEEP: 'list_customers_deep',
  CREATE_PRODUCT: 'create_product',
  GET_PRODUCT: 'get_product',
  LIST_PRODUCTS: 'list_products',
  LIST_PRODUCTS_DEEP: 'list_products_deep',
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
