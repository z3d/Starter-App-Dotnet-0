---
name: testing-strategy
description: Test organization, FsCheck property-based testing, Best.Conventional convention tests. Use when writing or modifying tests.
user-invocable: false
---

# Testing Strategy

## Test Organization

```
Tests/
├── Domain/              # Entity and value object tests
├── Application/         # Command/query handler tests
├── Integration/         # Full API integration tests
├── Conventions/         # Architectural rule enforcement
├── Fuzzing/             # Property-based tests (FsCheck)
└── TestBuilders/        # Test data builders
```

## Property-Based Testing (Fuzzing)

**FsCheck 2.16.6** with **FsCheck.Xunit** integration. Instead of hand-picked test values, FsCheck generates hundreds of random inputs to verify domain invariants hold universally.

**Key properties tested**:
- **Money**: Addition commutativity/associativity, subtract-inverse, subtract never produces negative amounts, negative rejection, currency validation
- **Email**: Whitespace rejection, valid acceptance, equality reflexivity, random string robustness
- **Product**: Stock update round-trip (`+n` then `-n` restores original), over-deduction throws
- **OrderItem**: Price invariant (`TotalIncGst == UnitIncGst * Qty`), GST rate bounds, ID/quantity validation
- **Order State Machine**: Valid transitions never throw, invalid transitions always throw, Reconstitute preserves all properties, order totals equal sum of item totals

**Usage**: Tests use `[Property]` attribute (FsCheck.Xunit) instead of `[Fact]`. FsCheck automatically shrinks failing inputs to minimal counterexamples.

## Convention Testing

**Architectural Rule Enforcement** with [Best.Conventional](https://github.com/andrewabest/Conventional) (`Conventions/` directory, 6 test classes: `NamingConventionTests`, `CqrsConventionTests`, `DomainConventionTests`, `ApiConventionTests`, `PersistenceConventionTests`, `DapperConventionTests`).

**Approach**: Use built-in conventions where possible. For structural checks not covered by built-ins, create custom conventions extending `ConventionSpecification` (from `Conventional.Conventions` namespace).

### Convention Categories

**Naming** — `Convention.NameMustEndWith`:
```csharp
endpointTypes.MustConformTo(Convention.NameMustEndWith("Endpoints"));
commandTypes.MustConformTo(Convention.NameMustEndWith("Command"));
```

**Encapsulation** — `Convention.PropertiesMustHavePrivateSetters` / `PropertiesMustHavePublicGetters`:
```csharp
entityTypes.MustConformTo(Convention.PropertiesMustHavePrivateSetters);
dtoTypes.MustConformTo(Convention.PropertiesMustHavePublicGetters);
```

**CQRS Data Access** — `Convention.MustNotTakeADependencyOn`:
```csharp
commandHandlers.MustConformTo(Convention.MustNotTakeADependencyOn(typeof(IDbConnection), "..."));
queryHandlers.MustConformTo(Convention.MustNotTakeADependencyOn(typeof(ApplicationDbContext), "..."));
```

**CQRS Handler Wiring** — `Convention.RequiresACorrespondingImplementationOf`:
```csharp
commandsWithResponse.MustConformTo(Convention.RequiresACorrespondingImplementationOf(
    typeof(IRequestHandler<,>), allTypes));
```

**Domain Integrity** — `Convention.MustHaveANonPublicDefaultConstructor` + custom specs:
```csharp
entityTypes.MustConformTo(Convention.MustHaveANonPublicDefaultConstructor);
valueObjectTypes.MustConformTo(new MustOverrideEqualsAndGetHashCodeConvention());
```

**Safety** — `Convention.VoidMethodsMustNotBeAsync`, `Convention.MustNotResolveCurrentTimeViaDateTime`

**Dapper SQL Quality** — Custom IL inspection convention (`DapperConventionTests`):
- Scans compiled query handler IL for `ldstr` opcodes (0x72) to extract SQL string literals
- Checks string literals against `Regex(@"SELECT\s+\*")` to prevent `SELECT *`
- Handles async state machine nested types (where string literals actually live in compiled IL)
- Allows `COUNT(*)` and other non-column-expanding uses of `*`

### Adding New Conventions

For checks covered by Best.Conventional built-ins, use `Convention.*` directly. For structural/wiring checks, extend `ConventionSpecification`:

```csharp
private class MyCustomConvention : ConventionSpecification
{
    protected override string FailureMessage => "description of what's expected";

    public override ConventionResult IsSatisfiedBy(Type type)
    {
        return /* check passes */
            ? ConventionResult.Satisfied(type.FullName!)
            : ConventionResult.NotSatisfied(type.FullName!, "specific failure reason");
    }
}
```

## Integration Testing

**Testcontainers for Realistic Testing**:

```csharp
public class ApiTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _msSqlContainer;
    private WebApplicationFactory<Program> _factory;

    public async Task InitializeAsync()
    {
        await _msSqlContainer.StartAsync();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<ConnectionStrings>(options =>
                    {
                        options.DefaultConnection = _msSqlContainer.GetConnectionString();
                    });
                });
            });
    }
}
```