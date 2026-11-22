# Owner Scoping in Handlers

Customer, Product, and Order are owner-scoped resources. The validated JWT establishes *who* the caller is; owner scoping establishes *which rows they may see or change*. Route metadata cannot do this — it can enforce identity, scope, and MFA before dispatch, but it has no idea who owns a specific row. So the checks live in query predicates and command handlers.

## The four convention rules

1. **`CommandHandlers_MustInjectOwnerOnlyPolicy` / `QueryHandlers_MustInjectOwnerOnlyPolicy`** — checks the constructor.
2. **`CommandHandlers_MustInvokeOwnerOnlyPolicy` / `QueryHandlers_MustInvokeOwnerOnlyPolicy`** — IL-scans (including async state machines) for an actual call to `GetRequiredScope()` or `Authorize(...)`. Injecting without invoking fails.
3. **`ResourceQueries_MustBeOwnerScoped`** — a query reading an owner-scoped table must implement `IOwnerScopedRequest`.
4. **`QueryHandlers_FilterEveryTenantAndOwnerTableByTheCallersScope`** — over every query handler, each `SELECT` over a table the migrations give a `tenant_id` carries `tenant_id = @TenantId`, and over one they give an `owner_subject` carries `owner_subject = @OwnerSubject`. The tables come from the scripts, not a list, so a derived project's own tables are held to it from their first migration.

Rules 2 and 4 exist because rules 1 and 3 are presence checks, and presence does not imply behaviour. That gap is the recurring finding class in this codebase — see `architecture-review`.

## Creates stamp, mutations check

The asymmetry is deliberate:

- **Commands that only create a new aggregate** (`CreateCustomerCommand`, `CreateProductCommand`) call `_ownerOnlyPolicy.GetRequiredScope()` and pass `ownerScope.OwnerSubject` / `ownerScope.TenantId` into the aggregate's constructor. They *establish* ownership, so there is nothing to authorize against yet. They are listed by name in `CqrsConventionTests.CommandsThatOnlyCreateANewAggregate`; the exemption is not a `Create*` prefix. `CreateOrderCommand` starts with "Create" but touches an existing customer and existing product stock, so it is a mutation.
- **Every other command** implements `IOwnerAuthorizedMutation` and calls `IOwnerOnlyPolicy.Authorize(...)` on the loaded aggregate before mutating it. The convention test requires the `Authorize` call itself for these handlers; `GetRequiredScope()` alone does not count.

Three pieces make the check a gate rather than a report. `OwnerAuthorizationBehavior` flags the request on the scoped `OwnerPolicyEvaluationTracker` before the handler runs, and `OwnerOnlyPolicy.Authorize` marks the tracker when it runs. `OwnerAuthorizationWriteGuard`, an EF interceptor attached in `AddPersistence`, throws before `SaveChanges` and before any `ExecuteUpdate`/`ExecuteDelete` command if the request is flagged and `Authorize` has not run. So a handler that forgets the check fails before anything is persisted, in every environment. The behavior also throws after the handler for the case where nothing was written at all. Because one `Authorize` marks the whole request, the guard also refuses, at `SaveChanges`, any added, changed or deleted row with an `OwnerSubject` and a `TenantId` that is not the caller's: authorizing one aggregate does not let the handler change another's. `ExecuteUpdate`/`ExecuteDelete` reach the database without passing the change tracker, so for those the single mark is still the only check.

## Query predicates

Every owned-table `SELECT` carries the owner predicate. Within a tenant, another owner's row is hidden from reads as not-found or an empty list, and a mutation of it returns 403. Another *tenant's* row is never loaded at all: row-level security hides it below the handler (`docs/DECISIONS.md`, "Tenant isolation is held by the database"), so every route answers for it exactly as for an id that does not exist, and a caller cannot learn which ids another tenant holds.

```sql
SELECT
    id AS "Id",
    name AS "Name",
    email AS "Email",
    date_created AS "DateCreated",
    is_active AS "IsActive"
FROM customers
WHERE id = @Id
  AND owner_subject = @OwnerSubject
  AND tenant_id = @TenantId
```

No `SELECT *` — `QueryHandlers_MustNotUseSelectStar` scans compiled IL for the literal. Dapper reads are wrapped in `PostgresRetryPolicy.ExecuteAsync` (`QueryHandlers_MustUsePostgresRetryPolicy`), and a miss returns `null` rather than throwing `EntityNotFoundException`; the endpoint maps null to 404.

## Caching interaction

Owner-scoped by-id caches include the verified tenant and subject in the cache key, and mutations invalidate the owner-scoped key only. The bare resource key has no writer — cacheable queries are all owner-scoped, and the protected surface is unreachable without an authenticated identity — so cached data cannot cross identities.

This is also why cache refresh-ahead recomputes on the caller rather than in a background scope: a background scope has no caller identity, so it would populate an owner-scoped key from the wrong one. See `docs/DECISIONS.md`.

## What this does not cover

Tenant isolation is not this: it is held by the database under the API's own login, and is never pruned in a multi-tenant product. Owner scoping is the per-person layer inside a tenant, and a derived project with no per-person rows prunes it. Token auth and owner scoping are the baseline, not the whole authorization story. Tenant-level permissions, resource-level roles, and domain-sensitive workflow rules still belong in application and domain code, with `IOwnerOnlyPolicy` as the floor.
