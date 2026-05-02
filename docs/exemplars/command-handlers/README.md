# Command Handler Exemplars

The consistency toolset (`StarterApp.Tests/Consistency/`) reports all three measurement layers for command handlers: structural distance from the exemplar centroid, AST/IL shingle similarity, and source-token embedding similarity. StarterApp keeps each command and its handler in the same `FooCommand.cs` source file; the cohort measures the handler type.

## Exemplars

<!-- Tests parse exemplar names from bold-backtick lines and dependency counts from "N dependencies". -->

**`CreateProductCommandHandler.cs`** — Simple create command. Builds a value object, creates a product aggregate, saves once, invalidates the product cache, logs the operation, and returns a DTO inline. Pinned as the basic write shape. 2 dependencies (ApplicationDbContext, ICacheInvalidator).

**`UpdateOrderStatusCommandHandler.cs`** — Tracked aggregate mutation without cache invalidation. Loads an order with its item collection, applies a domain state transition, saves once, and maps through `OrderMapper`. Pinned as the order-state transition shape. 1 dependency (ApplicationDbContext).

**`DeleteCustomerCommandHandler.cs`** — Void mutation with a guard query. Loads a tracked customer, checks for dependent orders, removes the aggregate, saves once, invalidates the customer cache, and logs both success and failure paths. Pinned as the delete-command shape. 2 dependencies (ApplicationDbContext, ICacheInvalidator).

## What the toolset measures

| Layer | Good at detecting | Not good at detecting |
|-------|------------------|----------------------|
| **Structural distance** (Mahalanobis with Ledoit-Wolf shrinkage, features z-scored against the cohort) | Shape anomalies — handlers with unusual complexity, dependencies, try/catch, private helpers, cache invalidation, or entity loads | Business correctness; domain/application tests own that |
| **AST shingles** (Jaccard) | Control-flow and method-call skeleton novelty | Intent; a familiar skeleton can still do the wrong thing |
| **Embedding similarity** (cosine via `SourceTokenEmbedder`) | Vocabulary outliers — domain types, methods, strings, and DTO/entity references that differ from the exemplars | Deep semantics; it is token overlap, not judgement |
| **Per-feature divergence** | Direct lists of members that differ on a single feature | Subtle multi-feature interactions |

## Features

| Feature | Why it's meaningful |
|---------|--------------------|
| `IlByteSize` | Complexity proxy across the handler and async state-machine methods |
| `ConstructorDependencyCount` | Handlers should stay small and dependency-light |
| `HasLogger` | StarterApp uses static Serilog calls; handlers without diagnostics are drift |
| `HasCacheInvalidator` | Mutations for cacheable entities should invalidate entity cache keys |
| `HasTryCatch` | Explicit exception handling is rare and worth review |
| `PrivateMethodCount` | Helper extraction changes handler shape and may signal complex orchestration |
| `EntityLoadCount` | EF read count distinguishes simple mutations from orchestration-heavy writes |
