# Query Handler Exemplars

The consistency toolset (`StarterApp.Tests/Consistency/`) reports all three measurement layers for query handlers: structural distance from the exemplar centroid, AST/IL shingle similarity, and source-token embedding similarity. StarterApp keeps each query and its handler in the same `FooQuery.cs` source file; the cohort measures the handler type.

## Exemplars

<!-- Tests parse exemplar names from bold-backtick lines and dependency counts from "N dependencies". -->

**`GetProductByIdQueryHandler.cs`** — Cached by-id lookup. Uses Dapper against `IDbConnection`, returns a nullable read model, and the query implements `ICacheable`. Pinned as the default by-id cacheable read shape. 1 dependency (IDbConnection).

**`GetAllProductsQueryHandler.cs`** — Paged list lookup. Uses `Page`/`PageSize`, SQL `OFFSET/FETCH`, and returns `IEnumerable<ProductReadModel>`. Pinned as the standard collection query shape. 1 dependency (IDbConnection).

**`GetOrderByIdQueryHandler.cs`** — Rich aggregate read. Uses two SQL statements: one root projection plus one item projection, then assembles an `OrderWithItemsReadModel`. Pinned as the intentionally complex by-id read shape. 1 dependency (IDbConnection).

## What the toolset measures

| Layer | Good at detecting | Not good at detecting |
|-------|------------------|----------------------|
| **Structural distance** (Mahalanobis with Ledoit-Wolf shrinkage, features z-scored against the cohort) | Query shape anomalies — extra dependencies, unexpected caching, list/single-row drift, SQL complexity | SQL correctness; integration tests and review own that |
| **AST shingles** (Jaccard) | Control-flow and method-call skeleton novelty across by-id, list, and rich-read shapes | Query intent; two SQL strings can compile to similar skeletons |
| **Embedding similarity** (cosine via `SourceTokenEmbedder`) | Vocabulary outliers — read-model names, SQL tokens, entity names, and method references | Business meaning; it is token overlap |
| **Per-feature divergence** | Direct lists of members that differ on a single feature | Subtle multi-feature interactions |

## Features

| Feature | Why it's meaningful |
|---------|--------------------|
| `IlByteSize` | Complexity proxy across handler and async state-machine methods |
| `ConstructorDependencyCount` | Queries should normally depend only on `IDbConnection` |
| `HasPagination` | StarterApp pagination is detected from `Page` and `PageSize` query properties |
| `IsCacheable` | Only by-id queries should be cacheable |
| `ReturnsList` | Distinguishes collection queries from single-row reads |
| `JoinCount` | Counts `JOIN` and `APPLY` string-literal usage as SQL composition complexity |
| `SqlStatementCount` | Multi-statement reads, like order plus order items, are intentionally different |
