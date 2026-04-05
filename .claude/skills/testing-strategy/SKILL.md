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
├── Infrastructure/      # Outbox, processor tests (Moq + InMemory DbContext)
├── Integration/         # Full API integration tests (Testcontainers)
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

### StarterApp.Tests — WebApplicationFactory + Testcontainers

The main test project uses `WebApplicationFactory<IApiMarker>` with Testcontainers for SQL Server and Respawn for per-test cleanup. This is the workhorse for API endpoint testing:

- **Speed**: In-process execution, no container orchestration overhead per test run
- **Test isolation**: Respawn resets the database in milliseconds between tests
- **Debugging**: In-process allows easy breakpoints
- **Scope**: Tests the API in isolation — Service Bus registration is a no-op when the connection string is absent

### StarterApp.AppHost.Tests — Aspire DistributedApplicationTestingBuilder

A separate test project for end-to-end distributed system testing. Spins up the full AppHost (SQL Server, Service Bus emulator, API, Functions, DbMigrator) using `DistributedApplicationTestingBuilder`:

- **When to use**: Testing cross-service communication (API → outbox → Service Bus → Functions)
- **Lifecycle**: `DistributedApplicationTestingBuilder` manages all container lifecycle — no manual Docker setup
- **HttpClient**: Use `app.CreateHttpClient("api")` to get a properly configured client
- **Speed**: Slow (spins up containers) — tag with `[Trait("Category", "Aspire")]` to exclude from fast CI runs
- **AppHost coupling**: Tests depend on the AppHost project — orchestration changes can affect tests

```csharp
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.StarterApp_AppHost>();
await using var app = await appHost.BuildAsync();
await app.StartAsync();

var httpClient = app.CreateHttpClient("api");
```

## Smoke Testing (Post-Deployment)

**Shell script** (`scripts/smoke-test.sh`) for verifying a live deployment. Complements integration tests — integration tests verify correctness in-process, smoke tests verify the deployed artifact works end-to-end.

```bash
./scripts/smoke-test.sh [BASE_URL]   # default: http://localhost:8080
./scripts/smoke-test.sh https://localhost:7286  # Aspire
```

**What it covers** (25 assertions):
- Health check (warn-only, Aspire health probes can fail externally)
- CRUD for products, customers, orders
- All validator rules (email format/length, currency, OrderId, status enum)
- Conflict responses (invalid state transitions, referential integrity)
- Not-found responses
- Order lifecycle (create → confirm → cancel)

**Design decisions**:
- Uses `curl` — zero dependencies, runs anywhere
- Unique test data per run (timestamp suffix) — idempotent, no cleanup needed
- Exits non-zero on failure — CI-friendly for post-deploy gates
- Auto-detects HTTPS and skips cert verification for dev certs

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