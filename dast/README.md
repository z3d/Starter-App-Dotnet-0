# DAST — Dynamic Application Security Testing

Runs [OWASP ZAP](https://www.zaproxy.org/) against the **running** StarterApp API to
find vulnerabilities that only appear at runtime (injection, missing security
headers, error-handling leaks, auth-surface exposure) — the complement to the
static convention/security tests in `StarterApp.Tests`.

## What it does

`run-dast.sh` is self-contained:

1. Boots a throwaway PostgreSQL container.
2. Runs DbUp migrations (`StarterApp.DbMigrator`).
3. Starts the API in `Development` (so OpenAPI is exposed and
   `GatewayIdentity:Mode=UnsignedDevelopment` is active).
4. Runs the ZAP Automation Framework plan (`automation.yaml`): inject identity
   headers → import OpenAPI → spider → passive scan → active scan → reports.
5. Fails the run if any alert is at/above the risk threshold (default `Medium`).
6. Tears everything down.

### Why identity headers are injected

The API has no built-in auth — it trusts a gateway and reads a projected identity
from request headers, optionally bound by a signed `X-Gateway-Assertion`. ZAP
can't compute the per-request HMAC the signed mode needs, so the scan runs the
API in **`UnsignedDevelopment`** mode, where the middleware still requires the
identity headers but skips the signature. The `replacer` job in `automation.yaml`
injects them on every request, so ZAP reaches the real `/api/v1` surface instead
of bouncing off `401`s. Assertion forgery / signature validation is already
covered by `GatewayIdentityIntegrationTests`.

## Requirements

- Docker (running)
- .NET 10 SDK
- `jq`, `curl`

## Usage

```bash
# Full self-contained run (boots DB + API, scans, gates, tears down)
dast/run-dast.sh

# Only fail on High-risk alerts (report still lists everything)
FAIL_RISK=High dast/run-dast.sh

# Scan an already-running instance (e.g. one you started via Aspire) and skip boot
SKIP_BOOT=1 TARGET_URL=http://localhost:5164 dast/run-dast.sh
```

### Knobs (environment variables)

| Var          | Default                          | Purpose                                   |
|--------------|----------------------------------|-------------------------------------------|
| `FAIL_RISK`  | `Medium`                         | Gate threshold: `High` \| `Medium` \| `Low` |
| `SKIP_BOOT`  | `0`                              | `1` = scan an existing API, don't boot one |
| `TARGET_URL` | `http://localhost:5164`          | API base URL                              |
| `API_PORT`   | `5164`                           | Port the booted API listens on            |
| `PG_PORT`    | `55432`                          | Host port for the throwaway PostgreSQL    |
| `ZAP_IMAGE`  | `ghcr.io/zaproxy/zaproxy:stable` | ZAP container image                       |

## Output

- `dast/reports/dast-report.html` — human-readable findings (open in a browser).
- `dast/reports/dast-report.json` — machine-readable; drives the pass/fail gate.
- `dast/reports/api.log` — API stdout/stderr from the scanned run.

The console prints an alert count per risk level and, on failure, the breaching
alerts.

## CI

Runs as its own workflow, `.github/workflows/dast.yml` — **nightly** (03:00 UTC)
and on demand via **workflow_dispatch** (which takes a `fail_risk` input). It is
deliberately *not* part of the per-PR `ci.yml` pipeline: the bounded-but-still-
~20-min active scan is too heavy to gate every PR. The script exits non-zero when
the gate trips, so the scheduled run goes red on a real finding; reports are
uploaded as the `dast-reports` artifact every run. The active scan is bounded
(`maxRuleDurationInMins`, `maxScanDurationInMins` in `automation.yaml`). Tune
`FAIL_RISK` to set how strict the gate is.

## ⚠️ Safety

The active scan sends real attack payloads and the runner provisions a disposable
database. **Only point this at the local throwaway instance or a dedicated test
environment — never at production or any shared/data-bearing environment.**
