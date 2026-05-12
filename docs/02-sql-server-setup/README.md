# Step 2: SQL Server and DbUp Migrations

## Overview

This project uses SQL Server for persistence and DbUp for deterministic schema migrations. Migrations run through the dedicated `StarterApp.DbMigrator` console app; the API never runs migrations during startup.

## Current Setup

- Aspire starts a SQL Server container and injects the `database` connection string.
- `StarterApp.AppHost` runs `StarterApp.DbMigrator` and makes the API wait for it to complete.
- Integration tests run migrations independently through the test fixture.
- Real deployments must run the migrator to completion before starting API replicas.

## Migrator Project

```
src/StarterApp.DbMigrator/
├── DatabaseMigrationEngine.cs
├── Program.cs
├── Scripts/
│   ├── 0001_CreateProductsTable.sql
│   ├── ...
│   └── 0019_AddOutboxProcessingClaims.sql
└── StarterApp.DbMigrator.csproj
```

Migration scripts are embedded resources and execute once, in filename order. Constraint names are explicit from script `0012` onward so future migrations can reference stable names.

## Running Migrations

### Aspire

```bash
dotnet run --project src/StarterApp.AppHost
```

AppHost starts SQL Server, runs the migrator, then starts the API after migration completion.

### Standalone

```bash
dotnet run --project src/StarterApp.DbMigrator -- --connection-string "<sql-server-connection-string>"
```

Use standalone migration runs for one-off local checks or deployment pipelines that execute migrations outside Aspire.

## Adding a Migration

1. Add the next numbered `.sql` file under `src/StarterApp.DbMigrator/Scripts/`.
2. Give every new primary key, foreign key, default, check constraint, and index an explicit deterministic name.
3. Keep scripts forward-only and idempotency-conscious; DbUp handles one-time execution through its journal table.
4. Run `dotnet test` so migration embedding and constraint naming conventions are checked.

## Troubleshooting

- **API cannot connect**: confirm the Aspire dashboard shows SQL Server healthy and the migrator completed successfully.
- **Migration failed**: inspect migrator logs and the DbUp journal table before editing the script.
- **Local SQL port changed**: use the Aspire dashboard resource details instead of assuming a fixed SQL host port.

## Next Step

Continue to [Step 3: Container Images](../03-docker-setup/README.md) for Dockerfile and image build validation.
