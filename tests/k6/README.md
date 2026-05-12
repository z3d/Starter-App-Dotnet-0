# k6 Load Tests

Performance and smoke tests for the StarterApp API.

## Prerequisites

Install [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/):

```bash
# macOS
brew install k6

# Windows
choco install k6

# Docker, from the repo root so relative imports resolve
docker run --rm -v "$PWD":/app -w /app grafana/k6 run tests/k6/smoke.js
```

## Running Tests

Start the API with Aspire and use the API endpoint shown in the dashboard:

```bash
dotnet run --project src/StarterApp.AppHost
K6_BASE_URL=https://localhost:<api-port> k6 run tests/k6/smoke.js
K6_BASE_URL=https://localhost:<api-port> k6 run tests/k6/load.js
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `K6_BASE_URL` | `http://localhost:8080` | API base URL (no trailing slash); override for Aspire's assigned API URL |
| `K6_AUTH_SUBJECT` | `k6-user` | Local gateway identity subject header |
| `K6_AUTH_TENANT` | `k6-tenant` | Local gateway identity tenant header |

## Thresholds

- Smoke test: all checks pass, zero HTTP errors, p95 response time under 2 seconds
- Load test: p95 under 500ms globally, p99 under 1500ms, HTTP error rate under 1%, check pass rate above 99%

## Notes

- Reset the test database between load test runs for consistent baselines.
- Seed products are created with 100,000 stock to avoid depletion during load tests.
- k6 sends normalized local `X-Authenticated-*` headers for `UnsignedDevelopment`, including `X-Authenticated-Amr` for write-route MFA proof.
- Smoke tests attempt cleanup but tolerate `409 Conflict` when order history prevents deletion.
