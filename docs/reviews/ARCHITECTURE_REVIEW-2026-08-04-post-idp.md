# Architecture Review — Post-IdP Conversion

**Date:** 2026-08-04

**Range:** `pre-idp-conversion..6f4479e` (28 commits, 121 changed files)

**Focus:** OIDC/JWT conversion, bearer challenges, identity conventions, and converted tooling

## Assessment

The conversion keeps the main security boundary sound: the API validates issuer, audience,
signature, and lifetime itself; claims are projected through `ICurrentUser`; API routes require
authorization and scopes; writes retain MFA and owner-policy enforcement. No high-severity defect
was found.

Four medium findings remain. Two affect deployed or cross-origin behavior, one leaves a stated
identity boundary mechanically unenforced, and one makes the converted smoke/DAST/performance
tooling depend on an undeclared runtime. The live architecture score was reduced from 8.0 to 7.7
until the fixes and their regression tests land.

## Findings

### 1. Bearer challenges are hidden from CORS clients

**Severity: Medium** | Files: `src/StarterApp.Api/Infrastructure/ServiceCollectionExtensions.cs:92`

The scope and MFA filters now return machine-actionable information in `WWW-Authenticate`, but the
CORS policy never exposes that response header. A browser client running on an allowed cross-origin
origin receives the response but cannot read the error, required scope, or `acr_values`; ASP.NET
Core exposes non-safelisted response headers only when the policy calls `WithExposedHeaders`.

**Fix:** Add `WithExposedHeaders("WWW-Authenticate")` to both CORS branches and add an integration
test that sends `Origin`, triggers a scope or MFA shortfall, and asserts
`Access-Control-Expose-Headers` plus the challenge value. See the
[ASP.NET Core CORS guidance](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0#set-the-exposed-response-headers).

### 2. Malformed authorities pass startup validation

**Severity: Medium** | Files: `src/StarterApp.Api/Infrastructure/ServiceCollectionExtensions.cs:110`

`ValidateOnStart` proves only that `Identity:Authority` is nonblank outside development-like
environments. Relative or malformed values, and an HTTP authority paired with
`RequireHttpsMetadata=true`, therefore pass the application-owned startup validation and fail only
when the JWT bearer handler constructs or retrieves its metadata address.

**Fix:** Validate that a configured authority is an absolute URI and that its scheme agrees with
`RequireHttpsMetadata` and the environment gate. Add option tests for relative, malformed, HTTP,
and valid HTTPS authorities.

### 3. The identity convention misses raw bearer-token reads

**Severity: Medium** | Files: `src/StarterApp.Tests/Conventions/ApiConventionTests.cs:112`

`ClaimsPrincipal_MustOnlyBeReadByIdentityInfrastructure` detects `ClaimsPrincipal` references and
retired gateway-header literals, but it does not detect `Request.Headers.Authorization`,
`HeaderNames.Authorization`, or the `"Authorization"` literal. Production code can consequently
read the raw bearer token outside `Infrastructure/Identity` without failing the convention, despite
the boundary stated in `AGENTS.md` and the test failure message.

**Fix:** Extend the IL scan to detect the Authorization header getter/constants outside the identity
namespace. Following the convention-test discipline, inject a representative violation, prove the
test fails with the intended message, revert it, touch the source, and prove the suite is green.

### 4. Authentication tooling has an undeclared Python dependency

**Severity: Medium** | Files: `scripts/smoke-test.sh:40`, `scripts/lib/dev-idp.sh:47`,
`dast/run-dast.sh:17`, `tests/k6/run-perf.sh:17`

The smoke test invokes `python3` for authority and token parsing before its later Python capability
probe and fallback. The shared IdP helper always invokes `python3`, while the DAST and performance
runners neither list Python as a requirement nor preflight it. Their default OIDC authentication
paths therefore fail on Python-less hosts and Windows execution-alias stubs even though the scripts
advertise other prerequisite sets.

**Fix:** Move a reusable JSON capability/parser helper before authentication and use it for authority
and token responses, or explicitly require and preflight Python in all three entry points. Preserve
the smoke test's non-Python fallback if portability remains an intended feature.

## Summary

| Finding | Severity | Impact |
|---|---|---|
| CORS hides bearer challenges | Medium | Cross-origin browser clients cannot perform the advertised step-up flow |
| Authority URI is insufficiently validated | Medium | Invalid identity configuration fails after startup instead of at validation |
| Authorization-header boundary is not enforced | Medium | A future raw-token read can bypass the mechanical architecture rule |
| OIDC tooling has an undeclared Python dependency | Medium | Default smoke, DAST, and performance flows fail on unsupported hosts |

## Fix Order

1. Expose `WWW-Authenticate` through CORS because the shipped browser-facing challenge flow is
   currently unreadable.
2. Validate the authority URI at startup to keep production identity configuration fail-fast.
3. Harden the Authorization-header convention before further identity code grows around it.
4. Make the tooling parser dependency explicit or portable.

## Verification Performed

- `git diff --check pre-idp-conversion..HEAD` — passed.
- `bash -n scripts/smoke-test.sh scripts/lib/dev-idp.sh dast/run-dast.sh tests/k6/run-perf.sh` — passed.
- Targeted convention, identity-options, and correlation tests — 123 passed.
- Broader fast test run — solution built; 548 tests passed and 55 database-backed tests could not
  start because the local Docker/Podman endpoint was unavailable. No assertion failure independent
  of that infrastructure error was observed.

## Resolution Standard

Each finding remains open until its regression test is proven red against an injected regression,
the regression is reverted, and the relevant suite passes. Update both this record and
`docs/ARCHITECTURE_REVIEW.md` when closing a finding.

## Resolution (2026-09-03)

| # | Fix | Regression test |
|---|---|---|
| 1 | Both CORS branches expose `WWW-Authenticate`, `X-Correlation-ID`, `Retry-After` | `CorsPolicyTests` (Development and Production branches) |
| 2 | `AuthorityIsWellFormed` startup validation: absolute http(s) URI, https unless `RequireHttpsMetadata=false` | `JwtIdentityOptionsTests` (six new cases) |
| 3 | Convention scans `IHeaderDictionary.get_Authorization`, `HeaderNames.Authorization`, and the literal outside `Infrastructure/Identity`; composition root exempt | Injected endpoint-side read failed the test, then reverted |
| 4 | Smoke test parses via its python3-or-grep helper from the first call; `dev-idp.sh` uses jq, then proven python3, then sed | Manual: token parse with python3 and jq removed from `PATH` |
