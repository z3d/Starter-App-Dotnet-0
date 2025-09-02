# Convention Tests Documentation

## Overview

This directory contains architectural convention tests that enforce coding standards and design patterns across the DockerLearning solution. These tests use the [Best.Conventional](https://github.com/andrewabest/Conventional) library by Andrew Best to automatically validate architectural rules and prevent code quality drift.

## Purpose

Convention tests serve as automated architectural guardrails that:

- **Enforce Consistency**: Maintain uniform naming conventions across all layers
- **Prevent Architectural Drift**: Catch violations early in the development process
- **Document Standards**: Serve as living documentation of architectural decisions
- **Enable Safe Refactoring**: Provide confidence when making large-scale changes
- **Reduce Code Review Overhead**: Automate the detection of common violations

## Architectural Principles Enforced

### 1. Clean Architecture Separation
- Endpoints handle HTTP concerns only
- Domain entities encapsulate business logic
- Value objects maintain immutability
- Services coordinate application workflows

### 2. Domain-Driven Design (DDD)
- Entities protect invariants through encapsulation
- Value objects are immutable by design
- Domain logic stays within domain boundaries
- Rich domain models over anemic data containers

### 3. CQRS Pattern Support
- Commands represent write operations
- Queries represent read operations
- Clear separation between read and write concerns
- Optimized data models for different use cases

### 4. Dependency Management
- Services follow dependency injection patterns
- Repositories abstract data access
- Interfaces define clear contracts
- Minimal coupling between layers

## Convention Rules

### Naming Conventions

| Type | Convention | Example | Rationale |
|------|------------|---------|-----------|
| Endpoint Definitions | Must end with "Endpoints" | `ProductEndpoints` | Minimal API organization standards |
| DTOs | Must end with "Dto" or "ReadModel" | `ProductDto`, `UserReadModel` | Clear data contract identification |
| Commands | Must end with "Command" | `CreateProductCommand` | CQRS write operation identification |
| Queries | Must end with "Query" | `GetProductQuery` | CQRS read operation identification |
| Services | Must end with "Service" | `ProductService` | Application layer identification |
| Repositories | Must end with "Repository" | `ProductRepository` | Data access abstraction |
| Test Classes | Must end with "Tests" or "Test" | `ProductEndpointsTests` | Test organization and discovery |

### Encapsulation Rules

| Type | Rule | Rationale |
|------|------|-----------|
| Domain Entities | Private setters required | Protects business invariants |
| Value Objects | Private setters required | Ensures immutability |
| DTOs/ReadModels | Public getters required | Enables serialization |

### Method Conventions

| Convention | Scope | Exceptions | Rationale |
|------------|-------|------------|-----------|
| Async methods must have "Async" suffix | All assemblies | Endpoints, MediatR handlers | .NET Framework Design Guidelines |

## Implementation Details

### Assembly Scope
- **API Assembly**: Endpoints, DTOs, Commands, Queries, Services
- **Domain Assembly**: Entities, Value Objects, Domain Services

### Compiler-Generated Type Filtering
The tests automatically exclude compiler-generated types such as:
- Async state machines (`d__` pattern)
- Lambda closures (`c__DisplayClass` pattern)
- Anonymous types (`<>` pattern)
- Nested compiler artifacts

### Error Handling
Convention violations trigger test failures with descriptive messages indicating:
- Which types violated the convention
- What the expected naming pattern should be
- Where the violations were found

## Running Convention Tests

### Local Development
```bash
# Run all convention tests
dotnet test --filter "FullyQualifiedName~ConventionTests"

# Run specific convention test
dotnet test --filter "FullyQualifiedName~ConventionTests.EndpointDefinitions_ShouldFollowNamingConventions"
```

### CI/CD Integration
These tests should be integrated into your build pipeline to catch violations early:

```yaml
# Example Azure DevOps pipeline step
- task: DotNetCoreCLI@2
  displayName: 'Run Convention Tests'
  inputs:
    command: 'test'
    projects: '**/*Tests.csproj'
    arguments: '--filter "FullyQualifiedName~ConventionTests" --logger trx --collect "XPlat Code Coverage"'
```

## Handling Convention Violations

### When a Test Fails
1. **Identify the Violation**: Read the error message to understand which types are violating conventions
2. **Assess the Need**: Determine if the violation is legitimate or if the code needs to be updated
3. **Choose Your Approach**:
   - **Fix the Code**: Rename classes/methods to follow conventions (recommended)
   - **Update the Test**: If the convention no longer applies (rare)
   - **Add Exclusion**: For special cases that should be excluded from conventions

### Example Violation Resolution

**Problem**: New class `ProductManager` violates service naming convention

**Solution Options**:
```csharp
// Option 1: Rename to follow convention (recommended)
public class ProductManagementService { }

// Option 2: Add exclusion to test (if justified)
var serviceTypes = ApiAssembly.GetTypes()
    .Where(t => /* existing filters */ &&
           !t.Name.Equals("ProductManager")); // Exclude specific class
```

## Adding New Conventions

### Step 1: Identify the Need
- New architectural pattern introduced
- Recurring code review feedback
- Team agreement on new standards

### Step 2: Implement the Test
```csharp
[Fact]
public void NewConvention_ShouldFollowExpectedPattern()
{
    var targetTypes = /* select relevant types */;
    
    targetTypes
        .MustConformTo(/* specify convention */)
        .WithFailureAssertion(Assert.Fail);
}
```

### Step 3: Document the Convention
- Update this README
- Add comprehensive XML documentation
- Communicate to the team

## Best Practices

### Test Organization
- Keep each convention test focused on a single rule
- Use descriptive test names that explain the convention
- Group related conventions logically

### Documentation
- Include architectural rationale for each convention
- Provide examples of correct and incorrect usage
- Explain exceptions and edge cases

### Maintenance
- Review conventions regularly as architecture evolves
- Update tests when introducing new patterns
- Remove obsolete conventions that no longer apply

## Troubleshooting

### Common Issues

**Issue**: Tests fail for legitimate code
**Solution**: Review if exclusions are needed or if the convention should be updated

**Issue**: Performance issues with large assemblies
**Solution**: Consider filtering types more aggressively before applying conventions

**Issue**: False positives from generated code
**Solution**: Enhance the `IsCompilerGenerated` method to catch new patterns

### Getting Help

1. **Check the Test Output**: Convention tests provide detailed failure messages
2. **Review This Documentation**: Most common scenarios are covered here
3. **Examine the Code**: Look at existing compliant code for examples
4. **Ask the Team**: Conventions are team decisions and may need discussion

## Contributing

When adding or modifying convention tests:

1. **Follow the Existing Pattern**: Use the same structure and documentation style
2. **Test Your Changes**: Ensure new conventions don't break existing code
3. **Update Documentation**: Keep this README current with any changes
4. **Consider Impact**: Think about how the convention affects the entire codebase

## References

- [Best.Conventional GitHub Repository](https://github.com/andrewabest/Conventional)
- [.NET Framework Design Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [Clean Architecture Principles](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [Domain-Driven Design](https://martinfowler.com/bliki/DomainDrivenDesign.html)
