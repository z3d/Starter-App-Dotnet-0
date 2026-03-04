# Convention Tests

Architectural convention tests using [Best.Conventional](https://github.com/andrewabest/Conventional) to enforce coding standards and design patterns. **23 tests** across 3 test classes.

## File Structure

| File | Tests | Purpose |
|------|-------|---------|
| `ConventionTestBase.cs` | — | Shared assembly refs (`DomainAssembly`, `ApiAssembly`) and `IsCompilerGenerated` helper |
| `TypeExtensions.cs` | — | `GetAllTypesImplementingOpenGenericType` extension (from Conventional.Samples pattern) |
| `NamingConventionTests.cs` | 9 | Naming suffix conventions for all type categories |
| `CqrsConventionTests.cs` | 6 | CQRS wiring, data access separation, dual interface enforcement |
| `DomainConventionTests.cs` | 8 | Encapsulation, constructors, equality, async safety, DateTime safety |

## Convention Categories

### Naming (NamingConventionTests — 9 tests)

Built-in `Convention.NameMustEndWith`:

| Type | Required Suffix |
|------|----------------|
| Endpoint definitions | `Endpoints` |
| DTOs | `Dto` or `ReadModel` |
| Commands | `Command` |
| Queries | `Query` |
| Command handlers | `CommandHandler` |
| Query handlers | `QueryHandler` |
| Services | `Service` |
| Validators | `Validator` |
| Test classes | `Tests` or `Test` |

### CQRS Data Access Separation (CqrsConventionTests — 2 tests)

Built-in `Convention.MustNotTakeADependencyOn`:

- Command handlers must NOT depend on `IDbConnection` — use `ApplicationDbContext` (EF Core) for writes
- Query handlers must NOT depend on `ApplicationDbContext` — use `IDbConnection` (Dapper) for reads

### CQRS Handler Wiring (CqrsConventionTests — 2 tests)

Built-in `Convention.RequiresACorrespondingImplementationOf`:

- Every `ICommand` must have a corresponding `IRequestHandler<,>` or `IRequestHandler<>`
- Every `IQuery<T>` must have a corresponding `IRequestHandler<,>`

Uses `GetAllTypesImplementingOpenGenericType` extension for handler discovery.

### CQRS Dual Interface (CqrsConventionTests — 2 tests)

Custom `ConventionSpecification` classes:

- Commands must implement both `ICommand` (marker) AND `IRequest<T>`/`IRequest` (mediator dispatch)
- Queries must implement both `IQuery<T>` (marker) AND `IRequest<T>` (mediator dispatch)
- Bidirectional: types in Commands/Queries namespace implementing `IRequest` must also have the marker

### Domain Integrity (DomainConventionTests — 6 tests)

| Convention | Built-in? | What it checks |
|-----------|-----------|---------------|
| `PropertiesMustHavePrivateSetters` | Yes | Entity and value object encapsulation |
| `PropertiesMustHavePublicGetters` | Yes | DTO/ReadModel serializability |
| `MustHaveANonPublicDefaultConstructor` | Yes | EF Core entity materialization |
| `MustOverrideEqualsAndGetHashCode` | Custom | Value object equality semantics |
| `AsyncMethodsMustHaveAsyncSuffix` | Yes | Async naming convention |
| `VoidMethodsMustNotBeAsync` | Yes | Prevents dangerous async void |

### Safety (DomainConventionTests — 2 tests)

| Convention | Built-in? | What it checks |
|-----------|-----------|---------------|
| `VoidMethodsMustNotBeAsync` | Yes | Async void crashes process on exception |
| `MustNotResolveCurrentTimeViaDateTime` | Yes | IL-level check for `DateTime.Now`/`DateTime.Today` |

## Approach

**Use built-in conventions where possible.** Best.Conventional provides conventions for naming, properties, dependencies, constructors, async methods, and DateTime usage. For structural checks not covered (interface implementation, method overrides, handler wiring), create custom conventions extending `ConventionSpecification` from the `Conventional.Conventions` namespace.

All tests must call `.WithFailureAssertion(Assert.Fail)` — without it, convention violations are silently ignored.

## Adding a New Convention

1. Choose the right file based on category (naming → `NamingConventionTests`, CQRS → `CqrsConventionTests`, domain → `DomainConventionTests`)

2. Check if Best.Conventional has a built-in for your check (see `Convention.*` static members)

3. If built-in exists:
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

4. If custom needed, extend `ConventionSpecification`:
```csharp
private class MyConvention : ConventionSpecification
{
    protected override string FailureMessage => "must satisfy rule";

    public override ConventionResult IsSatisfiedBy(Type type)
    {
        return /* passes */
            ? ConventionResult.Satisfied(type.FullName!)
            : ConventionResult.NotSatisfied(type.FullName!, "reason");
    }
}
```

## Running Tests

```bash
# All convention tests
dotnet test --filter "FullyQualifiedName~Convention"

# Specific category
dotnet test --filter "FullyQualifiedName~NamingConventionTests"
dotnet test --filter "FullyQualifiedName~CqrsConventionTests"
dotnet test --filter "FullyQualifiedName~DomainConventionTests"
```

## Compiler-Generated Type Filtering

The `IsCompilerGenerated` helper in `ConventionTestBase` excludes compiler-generated types (async state machines, lambda closures, display classes, nested types) that would cause false positives. Applied via `!IsCompilerGenerated(t)` in type filters where namespace-based selection might pick up generated types.
