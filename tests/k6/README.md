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

The runner boots a throwaway PostgreSQL **and a throwaway Redis**, runs DbMigrator, bulk-seeds
owner-scoped data for the k6 identity from `tests/k6/seed/perf-seed.sql` (20k customers / 20k
products / 20k orders with items for the primary `k6-user`, plus ~1k each across four alternate
owner identities so owner-scope predicates have real cardinality), starts the API in
`GatewayIdentity:Mode=UnsignedDevelopment`, and runs k6. Any threshold breach makes k6 exit
non-zero and fails the run; the summary export and API log land in `tests/k6/reports/`
(git-ignored, uploaded as the `k6-summary` artifact in CI).

Redis matters for the latency signal: the by-id customer/product queries are `ICacheable`
(10-min TTL). Without Redis the API falls back to an in-process memory cache (`Program.cs`
`AddDistributedMemoryCache`), so the by-id thresholds would measure an in-memory hit, not a
prod-like Redis round trip (and the stampede/single-flight refresh path is never exercised). The
runner passes `ConnectionStrings__redis=localhost:<REDIS_PORT>` so the API wires
`AddRedisDistributedCache("redis")`. Set `SKIP_REDIS=1` to opt out — by-id reads then fall back
to in-memory and the by-id p95 thresholds are not prod-meaningful.

The seed script is regression-tested by `PerfSeedScriptTests` (integration test, real
PostgreSQL): schema drift breaks that test at PR time instead of the nightly run.

### Scenarios (`load.js`)

- **browse** — list (page 1), get-by-id over the seeded population, orders-by-customer, plus
  **deep pagination** (random pages 1–800, exercising large-OFFSET `LIMIT/OFFSET` scans) and
  **orders-by-status** (the most index-sensitive seeded query).
- **orders** — create / get / status-update order placement.
- **contention** — low-VU scenario that repeatedly updates the *same* few product rows to drive
  the optimistic-concurrency (`xmin` → `409 Conflict`) path under real load. An expected 409 is
  registered as a non-failure via `http.setResponseCallback`. Correctness is unit-tested; this
  only confirms the 409 mapping surfaces under concurrency.

### Baseline / trend tracking

After k6 exits, the runner diffs `http_req_duration` p95/p99 against a committed baseline
(`tests/k6/baseline/summary-baseline.json`) if present, and **warns** on a regression beyond
`REGRESSION_PCT` (default 20%). It is warn-only by default so a noisy baseline cannot destabilize
the gate; set `REGRESSION_FAIL=1` to make a regression fatal. There is no committed baseline by
default — establish one by copying a known-good run:

```bash
cp tests/k6/reports/summary.json tests/k6/baseline/summary-baseline.json
```

See `tests/k6/baseline/README.md`.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `K6_BASE_URL` | `http://localhost:8080` | API base URL (no trailing slash); override for Aspire's assigned API URL |
| `K6_AUTH_SUBJECT` | `k6-user` | Local gateway identity subject header |
| `K6_AUTH_TENANT` | `k6-tenant` | Local gateway identity tenant header |
| `K6_MIN_LIST_ROWS` | `1` | Volume floor for list checks; the CI gate sets `20` after seeding so a fast-but-empty list response fails the run |

### `run-perf.sh` runner variables

| Variable | Default | Description |
|----------|---------|-------------|
| `SKIP_BOOT` | `0` | `1` = target an already-running instance, skip DB/Redis/API boot |
| `SKIP_SEED` | `0` | `1` = skip the bulk data seed |
| `SKIP_REDIS` | `0` | `1` = no Redis; by-id reads fall back to in-memory cache (by-id thresholds not prod-meaningful) |
| `REDIS_IMAGE` | `redis:7-alpine` | Throwaway Redis image |
| `REDIS_PORT` | `56379` | Host port for the throwaway Redis (distinct from `PG_PORT` 55433) |
| `BASELINE_FILE` | `tests/k6/baseline/summary-baseline.json` | Baseline summary to compare against (if present) |
| `REGRESSION_PCT` | `20` | % over baseline p95/p99 that counts as a regression |
| `REGRESSION_FAIL` | `0` | `1` = fail the run on a detected regression (default: warn only) |

## Thresholds

- Smoke test: all checks pass, zero HTTP errors, p95 response time under 2 seconds
- Load test: p95 under 500ms globally, p99 under 1500ms, HTTP error rate under 1%, check pass rate above 99%

## Notes

- Reset the test database between load test runs for consistent baselines.
- Seed products are created with 100,000 stock to avoid depletion during load tests.
- k6 sends normalized local `X-Authenticated-*` headers for `UnsignedDevelopment`, including `X-Authenticated-Amr` for write-route MFA proof.
- Smoke tests attempt cleanup but tolerate `409 Conflict` when order history prevents deletion.
