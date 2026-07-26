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
- **`Order` and `Product` carry `xmin` row-version tokens** (`uint RowVersion`, `.IsRowVersion()`). The resulting `DbUpdateConcurrencyException` → 409 mapping is load-bearing — the k6 write-contention scenario exercises it under load.
- **Enums persist as strings** via `HasConversion<string>()`.
- **Migrations run only through `StarterApp.DbMigrator`** (DbUp, embedded SQL, sequential `0001_...` naming). A script on disk that isn't embedded **silently never runs** — convention-tested.
- **Every constraint is explicitly named** — `pk_`, `fk_`, `df_`, `ck_`, `ix_` — *including defaults*. Anonymous constraints get server-generated names a later migration can't drop deterministically. → [reference/migrations.md](reference/migrations.md)
- **Connection resolution differs by design:** the API resolves only `database` and throws if absent (a misconfigured deployment fails loudly); DbMigrator falls back `database` → `postgres` → `DefaultConnection` for standalone runs.

## Depth

| Topic | Reference |
|---|---|
| Mapping code shapes, constraint-naming table, migration execution per environment, Aspire wiring | [reference/migrations.md](reference/migrations.md) |

## Related skills

- `ddd-implementation` — the entities being mapped
- `development-workflow` — Aspire and container troubleshooting
