export const BASE_URL = __ENV.K6_BASE_URL || 'http://localhost:8080';

export const AUTH_HEADERS = {
  'X-Authenticated-Subject': __ENV.K6_AUTH_SUBJECT || 'k6-user',
  'X-Authenticated-Principal-Type': 'User',
  'X-Authenticated-Tenant-Id': __ENV.K6_AUTH_TENANT || 'k6-tenant',
  'X-Authenticated-Scopes':
    'customers:read customers:write orders:read orders:write products:read products:write',
};

export const JSON_HEADERS = {
  headers: Object.assign({}, AUTH_HEADERS, { 'Content-Type': 'application/json' }),
};

export function uniqueSuffix() {
  return `${__VU}-${__ITER}-${Date.now()}`;
}
