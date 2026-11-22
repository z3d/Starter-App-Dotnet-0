---
name: data-access
description: EF Core mapping, xmin concurrency tokens, DbUp migrations and constraint naming, connection-string resolution. Use when modifying database schema, migrations, or data access code.
user-invocable: false
---

# Data Access

Entity mappings are per-entity `IEntityTypeConfiguration<T>` classes under `Data/Configurations/`, discovered by `ApplyConfigurationsFromAssembly()`. Read an existing configuration before adding one.

## The rules

- **Value objects map with `OwnsOne`** and explicit column names; a value object is never a `DbSet` (convention-enforced).
- **Aggregate collections use backing-field access** (`Navigation(...).UsePropertyAccessMode(PropertyAccessMode.Field)`), so the aggregate owns `_items` privately and EF populates via `.Include()`. No public setters on navigation collections.
- **Every aggregate root (`Customer`, `Order`, `Product`) carries an `xmin` row-version token** (`uint RowVersion`, `.IsRowVersion()`). The resulting `DbUpdateConcurrencyException` → 409 mapping is load-bearing — the k6 write-contention scenario exercises it under load.
- **Enums persist as strings** via `HasConversion<string>()`.
- **Migrations run only through `StarterApp.DbMigrator`** (DbUp, embedded SQL, sequential `0001_...` naming). A script on disk that isn't embedded **silently never runs** — convention-tested. Every run holds a session advisory lock (`DatabaseMigrationEngine.MigrationLockKey`) around the upgrade, so overlapping runs serialize instead of applying the same script twice. From script 0010 a migration is one phase of an expand/contract change and is named `Expand` or `Contract`; `MigrationCompatibilityConventionTests` refuses what the previous build cannot run against. → [reference/migrations.md](reference/migrations.md)
- **A tenant's table carries `tenant_id` and a forced tenant policy from the script that creates it; switching row-level security on for an existing table is a Contract step.** The previous build does not bind a tenant. The migrator's `ApplicationRoleSetup` grants every table to the application roles after each run, so a script never grants; `TenantRowSecurityTests` reads the catalog and fails a tenant table without its forced policy, a table with neither a `tenant_id` nor a listed reason, and a table the roles cannot use. A script's `DO` block uses `$$`, never `$name$`: DbUp reads `$name$` as one of its variables and fails the run. A backfill over a forced table as a non-superuser owner (the deployed migrator) sees no rows unless the script wraps it in `ALTER TABLE ... NO FORCE ROW LEVEL SECURITY` and `FORCE` again.
- **Every constraint is explicitly named** — `pk_`, `fk_`, `df_`, `ck_`, `ix_` — *including defaults*. Anonymous constraints get server-generated names a later migration can't drop deterministically. → [reference/migrations.md](reference/migrations.md)
- **Connection resolution differs by design:** the API resolves only `database` and throws if absent (a misconfigured deployment fails loudly); DbMigrator falls back `database` → `postgres` → `DefaultConnection` for standalone runs. The migrator's string is the server administrator's; the API's and the worker's name their own logins, which the migrator creates from `DatabaseLogins:Api` and `DatabaseLogins:Functions`.

## Depth

| Topic | Reference |
|---|---|
| Mapping code shapes, constraint-naming table, migration execution per environment, Aspire wiring | [reference/migrations.md](reference/migrations.md) |

## Related skills

- `ddd-implementation` — the entities being mapped
- `development-workflow` — Aspire and container troubleshooting
