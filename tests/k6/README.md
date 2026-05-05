# k6 Load Tests

Performance and smoke tests for the StarterApp API.

## Prerequisites

Install [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/):

```bash
# macOS
brew install k6

# Windows
choco install k6

# Docker (no install needed) — run from the repo root so relative imports resolve
docker run --rm -v "$PWD":/app -w /app grafana/k6 run tests/k6/smoke.js
```

## Running Tests

### Smoke Test (CI)

Quick validation that all endpoints respond correctly. 1 VU, 1 iteration, runs in seconds.

```bash
# Against Docker Compose (default: http://localhost:8080)
k6 run tests/k6/smoke.js

# Against Aspire or custom URL
K6_BASE_URL=https://localhost:7286 k6 run tests/k6/smoke.js
```

### Load Test (On-demand)

Sustained traffic with ramping VUs. Two concurrent scenarios:
- **Browse** (read-heavy): ramps to 200 VUs over ~5 minutes
- **Orders** (write-heavy): ramps to 50 VUs over ~5 minutes

```bash
K6_BASE_URL=http://localhost:8080 k6 run tests/k6/load.js
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `K6_BASE_URL` | `http://localhost:8080` | API base URL (no trailing slash) |
| `K6_AUTH_SUBJECT` | `k6-user` | Local gateway identity subject header |
| `K6_AUTH_TENANT` | `k6-tenant` | Local gateway identity tenant header |

## Thresholds

### Smoke Test
- All checks must pass (`rate==1.00`)
- Zero HTTP errors (`rate==0`)
- p95 response time under 2 seconds

### Load Test
- Global: p95 < 500ms, p99 < 1500ms
- List endpoints: p95 < 300ms
- Order creation: p95 < 800ms
- Order retrieval: p95 < 300ms
- HTTP error rate < 1%
- Check pass rate > 99%

## Notes

- **Database reset**: Reset the test database between load test runs for consistent baselines
- **Stock**: Seed products are created with 100,000 stock to avoid depletion during load tests
- **Gateway identity**: The API still assumes gateway-owned auth; k6 sends the normalized local `X-Authenticated-*` headers used when APIM is not present
- **Cleanup**: Smoke tests attempt cleanup but tolerate 409 (conflict) when order history prevents deletion
