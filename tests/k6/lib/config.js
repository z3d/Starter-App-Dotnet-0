export const BASE_URL = __ENV.K6_BASE_URL || 'http://localhost:8080';

export const JSON_HEADERS = {
  headers: { 'Content-Type': 'application/json' },
};

export function uniqueSuffix() {
  return `${__VU}-${__ITER}-${Date.now()}`;
}
