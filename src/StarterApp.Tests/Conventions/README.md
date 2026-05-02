# Convention Tests

Architectural convention tests using [Best.Conventional](https://github.com/andrewabest/Conventional) plus a few targeted reflection/source scanners. These tests encode mechanical rules that should fail fast when future changes drift from the template's intended shape.

## File Structure

| File | Purpose |
|------|---------|
| `ApiConventionTests.cs` | Endpoint/validator/handler dependency boundaries; DTO/read-model/response serializability; materialized response collections |
| `CachingConventionTests.cs` | `ICacheable` key/duration rules, deterministic same-id keys, and different-id collision prevention |
| `ConventionTestBase.cs` | Shared production assembly refs and compiler-generated type filtering |
| `CqrsConventionTests.cs` | CQRS data-access separation, command/query handler wiring, validator coverage, marker/request interface pairing |
| `DapperConventionTests.cs` | SQL literal inspection for `SELECT *` prevention and Dapper retry-policy usage |
| `DomainConventionTests.cs` | Domain encapsulation, constructors, value-object equality, async safety, `DateTime` safety, aggregate creation-event rules |
| `HousekeepingConventionTests.cs` | No direct `bin`/`obj` project references; no regions/XML docs/historical workaround comments in production code |
| `NamingConventionTests.cs` | Naming suffixes plus namespace locality for commands, queries, validators, handlers, DTOs, and read models |
| `PersistenceConventionTests.cs` | Entity registration, value-object mapping, enum string conversions, DbContext state, collection setters, migration script safety and embedding |
| `TypeExtensions.cs` | Open-generic type discovery helper from the Conventional.Samples pattern |

AppHost-specific conventions live in `src/StarterApp.AppHost.Tests`:

| File | Purpose |
|------|---------|
| `ServiceBusTopologyConventionTests.cs` | AppHost subscription filters, domain event contracts, and Azure Functions trigger wiring stay aligned |
| `ProductionAssemblyConventionTests.cs` | Async suffix, async-void, and `DateTime.Now` safety for AppHost, Functions, and ServiceDefaults assemblies |

## Core Rules

**Naming and locality**
- Endpoint definitions end with `Endpoints`
- DTOs/read models end with `Dto`/`ReadModel` and live in their expected namespaces
- Commands, queries, validators, and handlers live in mechanically discoverable namespaces
- Test classes end with `Tests` or `Test`

**CQRS**
- Command handlers use EF Core, not `IDbConnection`
- Query handlers use Dapper/`IDbConnection`, not `ApplicationDbContext`
- Every command/query has a handler and validator
- Commands and queries implement both mediator request interfaces and local marker interfaces

**API contracts**
- DTOs, read models, and response envelopes have public parameterless constructors
- Contract properties have public setters for serialization/model binding
- Contract collection properties are materialized, concrete collections such as `List<T>`
- DTOs/read models have no behavior

**Persistence**
- Domain entities are registered in `ApplicationDbContext`
- Value objects are not registered as `DbSet`
- Domain enum properties use EF string conversion
- Migration scripts are numbered, embedded resources, and name constraints explicitly

**Safety and housekeeping**
- Async methods end with `Async`; no `async void`
- Production code does not call `DateTime.Now`, `DateTime.Today`, or `DateTime.UtcNow`
- Project files do not reference build-output artifacts from `bin`/`obj`
- Production app code does not use regions, XML documentation comments, or historical workaround comments

## Adding a New Convention

1. Put deterministic "must always" rules in `Conventions/`.
2. Use `Convention.*` built-ins where possible.
3. Extend `ConventionSpecification` for structural checks not covered by Best.Conventional.
4. Always end with `.WithFailureAssertion(Assert.Fail)` for Best.Conventional checks.
5. Keep advisory similarity/drift checks in `Consistency/`, not here.

```csharp
[Fact]
public void MyTypes_MustFollowRule()
{
    var types = ApiAssembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && !IsCompilerGenerated(t));

    types
        .MustConformTo(Convention.SomeBuiltInConvention)
        .WithFailureAssertion(Assert.Fail);
}
```

## Running

```bash
dotnet test src/StarterApp.Tests/StarterApp.Tests.csproj --filter "FullyQualifiedName~Convention"
dotnet test src/StarterApp.AppHost.Tests/StarterApp.AppHost.Tests.csproj --filter "FullyQualifiedName~Convention"
```
