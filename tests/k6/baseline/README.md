# k6 Performance Baseline

`run-perf.sh` compares each run's key percentiles (`http_req_duration` p95/p99)
against `summary-baseline.json` here, if it exists, and warns (or fails when
`REGRESSION_FAIL=1`) on a regression beyond `REGRESSION_PCT` (default 20%).

There is intentionally **no committed baseline by default** — a baseline must be
a real, known-good run on representative hardware, not invented numbers. The
comparison is skipped (with a log line) when this file is absent, so the gate
still works without one.

## Establishing / refreshing the baseline

After a clean, known-good perf run:

```bash
cp tests/k6/reports/summary.json tests/k6/baseline/summary-baseline.json
```

Commit the result. Refresh it deliberately whenever an intentional, accepted
performance change shifts the percentiles — the same way event-contract
fixtures are refreshed. Do not refresh it to paper over a regression.

## Tuning

| Variable | Default | Effect |
|----------|---------|--------|
| `BASELINE_FILE` | `tests/k6/baseline/summary-baseline.json` | Path to the baseline summary |
| `REGRESSION_PCT` | `20` | % over baseline p95/p99 that counts as a regression |
| `REGRESSION_FAIL` | `0` | `1` makes a detected regression fail the run (default: warn only) |
