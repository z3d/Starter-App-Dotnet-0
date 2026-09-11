# Security review of the JWT identity path, 2026-09-12

Scope: the OIDC/JWT identity path, from the bearer handler configuration through
`JwtIdentityMiddleware`, the scope and MFA endpoint filters, `OwnerOnlyPolicy` and its
enforcement, the rate-limit partitioning, the payload capture boundary, and the convention and
integration tests that are supposed to hold all of that in place. Two passes: a manual read,
then the repo's `security-auditor` subagent with the manual findings withheld so it could not
anchor on them.

Nothing at Critical or High. Token validation (signature via JWKS, issuer from discovery,
audience, expiry, no local keys, no introspection), the single writer of `ICurrentUser`, the
claim parser's failure modes, the middleware order, CORS, and the archive redaction all held.

## Fixed the same day

| # | Finding | Fix |
|---|---|---|
| 1 | Owner authorization on writes was checked after `SaveChanges`, and outside Development/Testing it only logged. Two convention holes made that reachable: every `Create*` command was exempt by name, and "must invoke the policy" accepted `GetRequiredScope()` alone. `CreateOrderCommandHandler` was the concrete case. | `OwnerAuthorizationWriteGuard`, an EF interceptor attached in `AddPersistence`, throws before `SaveChanges` and before any `ExecuteUpdate`/`ExecuteDelete` if the request is an `IOwnerAuthorizedMutation` and `Authorize` has not run. The behavior flags the request before the handler and throws in every environment. `CreateOrderCommand` now implements `IOwnerAuthorizedMutation`; the exemption is an explicit two-command list; mutation handlers must call `Authorize` itself. |
| 2 | No authorization fallback policy, and the endpoint conventions only scanned `/api/v1`. A route mapped anywhere else would have been anonymous with no test failing. | `SetFallbackPolicy(RequireAuthenticatedUser)`. All probe and health endpoints moved into `ProbeEndpoints.MapProbeEndpoints` with `AllowAnonymous`. New convention test: every route either requires authorization or is on a closed list of six probe paths. |
| 3 | The "no raw claims outside Identity" test matched only IL whose declaring type was `ClaimsPrincipal`, so `httpContext.User.FindFirstValue("tid")` and `GetTokenAsync("access_token")` passed. | The scan now also flags `HttpContext.get_User`, `PrincipalExtensions`, `ClaimsIdentity`, `IPrincipal`, `IIdentity`, `IHttpContextAccessor`, and `GetTokenAsync`. `SaveToken = false`, so the raw token is not kept on the request. |
| 4 | No test used a second tenant; dropping the tenant half of the owner comparison passed the suite. | Same-subject/other-tenant cases in `OwnerOnlyPolicyTests` and `OwnerOnlyPolicyIntegrationTests` (read hidden, update and delete forbidden). |
| 5 | `ValidAlgorithms` was not pinned. | Pinned to RSA and ECDSA algorithms in `JwtIdentityOptions.AllowedSigningAlgorithms`, with a test. |

Also added: a missing-`sub` integration test, a test that `AddPersistence` attaches the guard,
and a test that the fallback policy is registered.

## Noted, not changed

| # | Finding | Why left |
|---|---|---|
| 6 | `IncludeErrorDetails` is on, so the 401 `WWW-Authenticate` header says why a token failed (wrong audience, expired, bad signature). | It only reflects values the caller sent. Turn it off outside Development if opaque 401s are wanted. |
| 7 | The anonymous rate-limit bucket keys on `RemoteIpAddress` with no forwarded-headers setup, so behind a proxy all anonymous traffic shares one bucket. | Deployment topology. If forwarded headers are added, configure `KnownProxies`/`KnownNetworks` or the key becomes attacker-chosen. |
| 8 | Cross-owner writes return 403 and missing rows 404, so sequential int ids can be enumerated across tenants. | Recorded decision in `DECISIONS.md`. Revisit by returning 404 for cross-owner mutations or moving to non-guessable ids. |
| 9 | The `Testing` environment name gets the same config relaxations as `Development`. | With no authority the handler has no keys and rejects every token, so it fails closed. A deployment guardrail, not a code change. |
| 10 | `ValidTypes` is not pinned. | The dev realm stamps the API audience on access tokens only, so audience validation already rejects ID tokens. An IdP that puts the API audience on ID tokens would need `ValidTypes` set to its access-token `typ`. |
