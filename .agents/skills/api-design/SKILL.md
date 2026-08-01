---
name: api-design
description: Adding or changing Minimal API endpoints — required route metadata, error mapping, pagination envelope. Use when creating or modifying API endpoints or endpoint filters.
user-invocable: false
---

# API Design

Endpoints live in `src/StarterApp.Api/Endpoints/`, one `IEndpointDefinition` per resource, auto-discovered by `MapApiEndpoints()`. Copy the shape of `CustomerEndpoints` — it is the reference implementation.

## The rules

`ApiConventionTests` enforces the first four from mapped endpoint metadata, so a violation fails the build rather than shipping an unprotected route:

- Every `/api/v1` group calls `.RequireAuthorization()` — the API validates OIDC/JWT bearer tokens itself.
- Every route declares `.RequireScope("{area}:read|write")`.
- Every non-GET route also calls `.SecuredBy2Fa()` (the validated token's `amr` claim must include `mfa`).
- Every handler binds a `CancellationToken` and forwards it to `mediator.SendAsync`.
- Health/liveness probes stay anonymous — orchestrator probes carry no bearer token.
- Handlers are `private static`, dispatch to the mediator, and shape the HTTP result — nothing else. A query returning `null` becomes `Results.NotFound()`.
- Declare `Produces`/`ProducesProblem` — the OpenAPI doc feeds the DAST scan, so its accuracy is load-bearing.
- **Don't catch exceptions in an endpoint to shape a status.** One closed mapping table in `ExceptionHandlingMiddleware` owns all of it; add to the table instead. Bare BCL exceptions map to 500 on purpose (`docs/DECISIONS.md`).
- **Middleware for global concerns, endpoint filters for route-specific ones.** Middleware runs once per request before routing; filters run after routing and parameter binding, only for matched endpoints.
- **Pagination:** `page`/`pageSize`, fetch `pageSize + 1`, trim, return `PagedResponse<T>` (`{ data, hasMore }`). No COUNT per list call — if a frontend needs a total, add a count endpoint.

## Depth

| Topic | Reference |
|---|---|
| Full endpoint example, auto-discovery mechanism, filter example, Problem Details setup | [reference/endpoint-patterns.md](reference/endpoint-patterns.md) |

## Related skills

- `cqrs-patterns` — the handlers behind the routes
- `testing-strategy` — integration tests for the endpoint surface
