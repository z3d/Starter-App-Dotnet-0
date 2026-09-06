# DAST — Dynamic Application Security Testing

Runs [OWASP ZAP](https://www.zaproxy.org/) against the **running** StarterApp API to
find vulnerabilities that only appear at runtime (injection, missing security
headers, error-handling leaks, auth-surface exposure) — the complement to the
static convention/security tests in `StarterApp.Tests`.

## What it does

`run-dast.sh` is self-contained:

1. Boots a throwaway PostgreSQL container.
2. Runs DbUp migrations (`StarterApp.DbMigrator`).
3. Seeds owner-scoped data (`seed/dast-seed.sql`) for the scanned identity
   (`dast-user-01`) plus a second owner (`dast-user-02`) used by the cross-owner
   probe — so by-id/list endpoints return real rows instead of empty results.
4. Starts the API in `Development` (so OpenAPI is exposed and
   tokens are validated against a throwaway dev Keycloak), with the per-identity
   rate limit lifted (`RateLimiting__PermitLimit=1000000`).
5. Runs the ZAP Automation Framework plan (`automation.yaml`): inject a bearer token
   headers → import OpenAPI → spider → passive scan → active scan → reports.
6. Fails the run if any alert is at/above the risk threshold (default `Medium`),
   if ZAP itself failed/hung (non-zero/non-WARN exit), or if the scan discovered
   fewer than `DAST_MIN_URLS` URLs (a dead/throttled scan can't pass green).
7. Runs a scripted cross-owner IDOR probe (`curl`) asserting owner-02's resources
   stay hidden (404/empty) or forbidden (403) under owner-01's identity.
8. Tears everything down.

### Why the rate limit is lifted

The `replacer` job injects a single identity on every request, so the whole
~20-min active scan shares one per-identity rate-limit bucket. At the default
`PermitLimit=100` per 60s, everything after ~100 requests is `429`'d before
reaching a handler, gutting active-scan coverage. The boot sets
`RateLimiting__PermitLimit=1000000` so the scan measures the API, not the limiter
(the k6 perf gate lifts it the same way, for the same single-identity reason).

### Why a coverage floor and exit-code guard

A trailing `|| true` on the ZAP run plus a "fail only if the report is missing"
gate would let a ZAP hang/OOM/half-run pass green with 0 alerts. The runner now
captures ZAP's autorun exit code (treating anything but `0`/`2` as a hard infra
failure, since the plan sets `failOnError: true`) and enforces a URL-discovery
coverage floor (`DAST_MIN_URLS`, default 5) parsed from the ZAP openapi/spider
job summaries — the DAST analogue of the k6 gate's `K6_MIN_LIST_ROWS` volume
floor. The floor counts URLs the scan *discovered*, not URIs that produced an
alert: a hardened app can be crawled in full yet alert on only a few URLs, so an
alert-instance count would conflate "clean app" with "scan reached nothing".

### Why a cross-owner IDOR probe

ZAP injects one fully-authorized identity, so a handler that dropped its
`IOwnerOnlyPolicy` check (cross-owner read or mutation) is structurally invisible
to the scan — every request it makes is in-scope by construction. The seed plants
owner-02 resources at fixed ids; the probe requests them under owner-01's identity
and fails the build if a cross-owner read returns the row or a cross-owner
mutation is accepted (the IDOR regression signal).

### Why a bearer token is injected

The API validates OIDC/JWT bearer tokens itself; without one every `/api/v1`
request 401s at the door and the scan never reaches the application surface.
`run-dast.sh` boots the dev Keycloak with the committed realm, mints a
`dast-user-01` access token, and the `replacer` job in `automation.yaml` attaches
it as `Authorization: Bearer` on every request. Negative-path token validation
(expiry, audience, issuer, signature, scope, amr) is covered by
`JwtIdentityIntegrationTests`.

## Requirements

- Docker or Podman (running; the script prefers `docker`, falls back to `podman`, override with `CONTAINER_CLI=`)
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
| `FAIL_RISK`     | `Medium`                         | Gate threshold: `High` \| `Medium` \| `Low` |
| `SKIP_BOOT`     | `0`                              | `1` = scan an existing API, don't boot one |
| `SKIP_SEED`     | `0`                              | `1` = skip the owner-scoped data seed (also skips the IDOR probe) |
| `DAST_MIN_URLS` | `5`                              | Minimum URLs the scan must discover, else fail |
| `TARGET_URL`    | `http://localhost:5164`          | API base URL                              |
| `API_PORT`      | `5164`                           | Port the booted API listens on            |
| `PG_PORT`       | `55432`                          | Host port for the throwaway PostgreSQL    |
| `ZAP_IMAGE`     | `ghcr.io/zaproxy/zaproxy:stable` | ZAP container image                       |

## Output

- `dast/reports/dast-report.html` — human-readable findings (open in a browser).
- `dast/reports/dast-report.json` — machine-readable; drives the pass/fail gate.
- `dast/reports/api.log` — API stdout/stderr from the scanned run.
- `dast/reports/zap-autorun.log` — ZAP Automation Framework stdout; the coverage
  gate parses the openapi/spider URL-discovery counts from it.

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

## Scope: Development posture only (by design)

This scan runs the API over plain HTTP with a dev-realm token, not the shipped
production posture (https with `RequireHttpsMetadata`, HSTS, a production IdP).
That is an intentional scope decision, not an oversight:

- Token *validation* is identical to production — same JwtBearer handler, same
  issuer/audience/signature/expiry checks, asymmetric keys via JWKS. Only the
  issuing realm and the transport differ, so the scan exercises the real
  application/handler surface behind real authentication.
- The signed-assertion / `Required`-mode path — assertion forgery, signature
  validation, expiry, wrong-audience/path/key rejection — is covered statically by
  `JwtIdentityIntegrationTests` (`src/StarterApp.Tests/Integration/JwtIdentityIntegrationTests.cs`),
  not by this dynamic scan.

Consequence: the production transport posture (HSTS, signed-assertion enforcement)
is **not** dynamically scanned here. Treat ZAP findings as covering the
application surface; the edge/transport
hardening is verified elsewhere.

## ⚠️ Safety

The active scan sends real attack payloads and the runner provisions a disposable
database. **Only point this at the local throwaway instance or a dedicated test
environment — never at production or any shared/data-bearing environment.**


Runner regressions can be run without containers or network access:

```bash
bash dast/tests/run-tests.sh
```

`TARGET_URL` preserves the HTTP(S) scheme, hostname, explicit port, and optional base path.
Only loopback hosts are translated to `host.docker.internal` for ZAP's container; HTTPS remains
HTTPS. Credentials, query strings, and fragments are rejected as API base URLs. With `SKIP_BOOT=1`,
supply a `DAST_TOKEN` valid for that target. The cross-owner list probe requires HTTP 200 and an
actual empty `data` array; error responses cannot satisfy it.
