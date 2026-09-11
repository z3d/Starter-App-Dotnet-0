# Owner Scoping in Handlers

Customer, Product, and Order are owner-scoped resources. The validated JWT establishes *who* the caller is; owner scoping establishes *which rows they may see or change*. Route metadata cannot do this — it can enforce identity, scope, and MFA before dispatch, but it has no idea who owns a specific row. So the checks live in query predicates and command handlers.

## The four convention rules

1. **`CommandHandlers_MustInjectOwnerOnlyPolicy` / `QueryHandlers_MustInjectOwnerOnlyPolicy`** — checks the constructor.
2. **`CommandHandlers_MustInvokeOwnerOnlyPolicy` / `QueryHandlers_MustInvokeOwnerOnlyPolicy`** — IL-scans (including async state machines) for an actual call to `GetRequiredScope()` or `Authorize(...)`. Injecting without invoking fails.
3. **`ResourceQueries_MustBeOwnerScoped`** — a query reading an owner-scoped table must implement `IOwnerScopedRequest`.
4. **`OwnerScopedQueryHandlers_MustFilterSqlByOwnerScope`** — every owned-table `SELECT` literal must carry `owner_subject = @OwnerSubject AND tenant_id = @TenantId`.

Rules 2 and 4 exist because rules 1 and 3 are presence checks, and presence does not imply behaviour. That gap is the recurring finding class in this codebase — see `architecture-review`.

## Creates stamp, mutations check

The asymmetry is deliberate:

- **Commands that only create a new aggregate** (`CreateCustomerCommand`, `CreateProductCommand`) call `_ownerOnlyPolicy.GetRequiredScope()` and pass `ownerScope.OwnerSubject` / `ownerScope.TenantId` into the aggregate's constructor. They *establish* ownership, so there is nothing to authorize against yet. They are listed by name in `CqrsConventionTests.CommandsThatOnlyCreateANewAggregate`; the exemption is not a `Create*` prefix. `CreateOrderCommand` starts with "Create" but touches an existing customer and existing product stock, so it is a mutation.
- **Every other command** implements `IOwnerAuthorizedMutation` and calls `IOwnerOnlyPolicy.Authorize(...)` on the loaded aggregate before mutating it. The convention test requires the `Authorize` call itself for these handlers; `GetRequiredScope()` alone does not count.

Two pieces make the check a gate rather than a report. `OwnerAuthorizationBehavior` flags the request on the scoped `OwnerPolicyEvaluationTracker` before the handler runs, and `OwnerOnlyPolicy.Authorize` marks the tracker when it runs. `OwnerAuthorizationWriteGuard`, an EF interceptor attached in `AddPersistence`, throws before `SaveChanges` and before any `ExecuteUpdate`/`ExecuteDelete` command if the request is flagged and `Authorize` has not run. So a handler that forgets the check fails before anything is persisted, in every environment. The behavior also throws after the handler for the case where nothing was written at all.

## Query predicates

Every owned-table `SELECT` carries the owner predicate. Cross-owner reads are hidden as not-found or an empty list; cross-owner mutations return 403. The difference matters: a 404 on a read does not confirm the resource exists, while a 403 on a write is the accurate answer to an authenticated caller acting outside their scope.

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

Token auth and owner scoping are the baseline, not the whole authorization story. Tenant-level permissions, resource-level roles, and domain-sensitive workflow rules still belong in application and domain code, with `IOwnerOnlyPolicy` as the floor.
