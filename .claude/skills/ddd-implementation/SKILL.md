---
name: ddd-implementation
description: Domain entities, value objects, ownership stamping, and the test-only Reconstitute pattern. Use when creating or modifying domain models.
user-invocable: false
---

# Domain Model Implementation

Read `src/StarterApp.Domain/` before adding a type; `Product`, `Customer`, `Order`, `Money`, and `Email` establish the idiom.

## The rules

- **Entities:** private setters, protected parameterless constructor for EF, a public constructor that establishes every invariant, behaviour methods for every mutation. No public `SetId()`.
- **Owner-scoped aggregates take `ownerSubject`/`tenantId` in the public constructor** and validate via `OwnershipDefaults.Validate(...)`. An aggregate constructible without ownership will eventually be constructed without it — `DomainConventionTests.OwnerScopedAggregates_MustNotExposeOwnerlessPublicConstructors` closes that door.
- **Aggregates overriding `RecordCreation()` mint `Guid.CreateVersion7()` in the constructor.** Creation events are captured into the outbox *before* `SaveChanges`, so a database-generated Id doesn't exist yet.
- **Value objects: static factory that validates and normalizes**, `Equals`/`GetHashCode` overridden plus `IEquatable<T>`, embedded in the owning entity — never a parallel entity/value pair. Normalization is the easy-to-skip part: `Money.Create` rounds to whole minor units, upper-cases, and requires exactly three ISO letters.
- **`Reconstitute` is test-only.** It exists so property tests can build aggregates in arbitrary states without walking the state machine. Production handlers load tracked entities — `AsNoTracking` + `Reconstitute` + `Update` marks every column modified and loses concurrent writes.
- **`DateTimeOffset`, never `DateTime`** (convention-enforced).

## Depth

| Topic | Reference |
|---|---|
| Full entity/value-object code shapes, the Reconstitute rationale | [reference/model-shapes.md](reference/model-shapes.md) |

## Related skills

- `cqrs-patterns` — how handlers drive these aggregates
- `data-access` — how EF maps entities and embedded value objects
