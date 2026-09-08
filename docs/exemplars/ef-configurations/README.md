# EF Configuration Exemplars

The consistency toolset (`StarterApp.Tests/Consistency/`) reports two measurement layers for EF Core `IEntityTypeConfiguration<T>` implementations: structural distance from the exemplar centroid and per-feature divergence. These exemplars define the intended mapping shapes after moving configuration out of `ApplicationDbContext`.

## Exemplars

<!-- Tests parse exemplar names from bold-backtick lines and dependency counts from "N dependencies". -->

**`ProductConfiguration.cs`** — Value-object mapping shape. Owns the `Money` price value object, configures amount/currency columns, and applies scalar constraints for name and description. Pinned as the standard entity-plus-value-object mapping. 0 dependencies (EF configurations don't inject services).

**`OrderConfiguration.cs`** — Aggregate-root mapping shape. Configures client-assigned `Guid` identity, enum conversion, child collection relationship, cascade delete, and field-backed navigation access. Pinned as the rich aggregate mapping shape. 0 dependencies.

**`OutboxMessageConfiguration.cs`** — Infrastructure table mapping shape. Configures required payload/type columns, retry default, optional error field, and the filtered unprocessed-message index. Pinned as the non-domain persistence mapping shape. 0 dependencies.

## What the toolset measures

| Layer | Good at detecting | Not good at detecting |
|-------|------------------|----------------------|
| **Structural distance** (Mahalanobis with Ledoit-Wolf shrinkage, features z-scored against the cohort) | Mapping shape anomalies — unusual value-object count, index count, conversion count, child relationships, or property width | Database correctness; migrations and persistence convention tests own that |
| **Per-feature divergence** | Direct lists of members that differ on a single feature | Subtle multi-feature interactions |

## Features

| Feature | Why it's meaningful |
|---------|--------------------|
| `IlByteSize` | Complexity proxy across the configuration type |
| `OwnsOneCount` | Value-object embedding depth |
| `HasIndexCount` | Query and processing surface design |
| `PropertyConfigCount` | Entity width and scalar configuration density |
| `HasConversionCount` | Enum/value-converter density |
| `HasManyCount` | Child collection mapping rarity detector |
