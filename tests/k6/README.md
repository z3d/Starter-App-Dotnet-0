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

## CI Performance Gate

`.github/workflows/perf.yml` runs nightly (02:00 UTC) and on `workflow_dispatch`. It calls
`tests/k6/run-perf.sh`, which is also the one-command local reproduction:

```bash
tests/k6/run-perf.sh                            # boot PG + migrate + seed + API + load.js
K6_SCRIPT=tests/k6/smoke.js tests/k6/run-perf.sh   # quick harness rehearsal with the smoke test
```

The runner boots a throwaway PostgreSQL, runs DbMigrator, bulk-seeds owner-scoped data for the
k6 identity from `tests/k6/seed/perf-seed.sql` (20k customers / 20k products / 20k orders with
items — so list, pagination, and index paths run at realistic volume), starts the API in
`GatewayIdentity:Mode=UnsignedDevelopment`, and runs k6. Any threshold breach makes k6 exit
non-zero and fails the run; the summary export and API log land in `tests/k6/reports/`
(git-ignored, uploaded as the `k6-summary` artifact in CI).

The seed script is regression-tested by `PerfSeedScriptTests` (integration test, real
PostgreSQL): schema drift breaks that test at PR time instead of the nightly run.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `K6_BASE_URL` | `http://localhost:8080` | API base URL (no trailing slash); override for Aspire's assigned API URL |
| `K6_AUTH_SUBJECT` | `k6-user` | Local gateway identity subject header |
| `K6_AUTH_TENANT` | `k6-tenant` | Local gateway identity tenant header |
| `K6_MIN_LIST_ROWS` | `1` | Volume floor for list checks; the CI gate sets `20` after seeding so a fast-but-empty list response fails the run |

## Thresholds

- Smoke test: all checks pass, zero HTTP errors, p95 response time under 2 seconds
- Load test: p95 under 500ms globally, p99 under 1500ms, HTTP error rate under 1%, check pass rate above 99%

## Notes

- Reset the test database between load test runs for consistent baselines.
- Seed products are created with 100,000 stock to avoid depletion during load tests.
- k6 sends normalized local `X-Authenticated-*` headers for `UnsignedDevelopment`, including `X-Authenticated-Amr` for write-route MFA proof.
- Smoke tests attempt cleanup but tolerate `409 Conflict` when order history prevents deletion.
