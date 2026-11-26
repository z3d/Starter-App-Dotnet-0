---
paths:
  - "src/**"
  - "dev/**"
  - "web/src/**"
  - "infra/**"
  - "Directory.Packages.props"
  - "docs/DECISIONS.md"
---
# Recorded decisions

Moved from `AGENTS.md` unchanged.

Each of these was chosen against a reasonable alternative and carries a **re-add trigger** — the specific fact that would justify revisiting it. Full rationale in [`docs/DECISIONS.md`](docs/DECISIONS.md); don't reverse one without hitting its trigger.

| Decision | Re-add trigger |
|---|---|
| Exceptions are the app-wide error model — no `Result<T>`/ErrorOr | A component with expected, frequent, locally-handled failures may use `Result<T>` internally, never across the mediator/endpoint boundary |
| The domain event *is* the wire contract — no separate `IIntegrationEvent`, no in-process dispatcher | A domain event needs a property the external contract shouldn't carry, or you need a same-transaction in-process reaction |
| One `domain-events` topic: it carries one kind of message (domain events) from one sender (the outbox); each consumer is a subscription with a correlation filter per event type | A second kind of message: commands go on a queue, events from or for another system get their own topic; or an event type needing its own topic settings, or modules becoming separate deployables |
| Payload capture runs as the *first* middleware, ahead of exception handling and rate limiting | None — moving it behind the rate limiter blinds the audit trail to rejected traffic and needs a new recorded decision |
| AppHost + AppHost.Tests are exempt from `packages.lock.json` | None — the Aspire SDK injects host-RID packages, so no single lock file satisfies locked mode across platforms |
| NuGet signature validation is deliberately off; feed restriction + lock hashes cover it | A clean-cache Linux restore passes across the full package set with `trustedSigners` enabled |
| Reads go through Dapper on a transient `IDbConnection`, not EF Core raw SQL (`SqlQuery<T>`/`FromSql`) | A requirement that *all* SQL flow through EF interceptors/diagnostics, or Dapper blocking a .NET/Npgsql upgrade — converge in one change that also rewrites `DapperConventionTests` |
| Service Bus subscribers get no ordering guarantee (`maxConcurrentCalls: 16`, no sessions) | — subscriber implementations must tolerate out-of-order delivery |
| Dapper reads retry through the hand-rolled `PostgresRetryPolicy` with a total delay budget, not Polly | Polly gains a total-delay budget, or the read path needs a second resilience concern (breaker, hedging) |
| The `Consistency/` test suite is advisory and human-read; builds never gate on a distance | Two consecutive dated reviews record that no report line informed a finding |
| Managed identity everywhere when published: Seq is not published, Redis becomes Azure Managed Redis (keys disabled), Keycloak's admin password is a generated secret | None — a dependency that cannot take a managed identity is replaced or dropped, never given a key |
| One OIDC issuer: `Identity:Authority`, plus `Identity:MetadataAddress` when discovery is fetched over another hostname | One API must accept tokens from a second issuer — an `Authentication:Issuers[]` list and a policy scheme that forwards on the token's issuer, never a second `AddJwtBearer` |
| The worker sends the application's requests through the mediator in its own process (`IWorkerOperations`), never over HTTP to the API and never with SQL of its own on the application's tables; `src/StarterApp.Application` holds what both hosts run, the web host keeps `Program.cs` and the endpoints, and the worker's login holds only `WorkerGrants` | None — a request the worker needs is a method on `IWorkerOperations`, an entry in `WorkerRequests` and `WorkerGrants`, and a case in `WorkerOperationsTests` |
| Tenant isolation is the database's: forced row-level security on every table with a `tenant_id`, the API and worker on logins that cannot bypass it, the tenant bound on every connection open; never pruned in a multi-tenant product | None — a deployment that cannot give the API a login of its own is not supported |
| The Entra token is the database credential in every process; a password-less connection string means the hosting identity (the named user, or the token's own principal when none is named), and raw `new NpgsqlConnection` / `UseNpgsql(string)` are banned | A PostgreSQL host with no Entra support (self-hosted) — already handled: a string with a password is used as given; never add a second credential mechanism |
